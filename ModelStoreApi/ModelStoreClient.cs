using Microsoft.Extensions.Options;
using ModelStoreApi.Models;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;

namespace ModelStoreApi
{
    public class ModelStoreClient: IDisposable
    {
        private static readonly BsonDocument s_trainingStatsProjection = new("$project",
                new BsonDocument
                    {
                        { "datetime", 1 },
                        { "_id", 1 },
                        { "module", 1 },
                        { "class", 1 },
                        { "args", 1 },
                        { "kwargs", 1 },
                        { "tag", 1 },
                        { "trainable_params", 1 },
                        { "min_val_loss",
                new BsonDocument("$min",
                new BsonDocument("$ifNull",
                new BsonArray
                                {
                                    "$training_history.val_loss",
                                    0
                                })) },
                        { "max_val_accuracy",
                new BsonDocument("$max",
                new BsonDocument("$ifNull",
                new BsonArray
                                {
                                    "$training_history.val_accuracy",
                                    0
                                })) },
                        { "status", 1 }
                    });


        private readonly MongoClient _mongoClient;
        private readonly ILogger<ModelStoreClient> _logger;
        private readonly IMongoDatabase _modelDb;
        private readonly IMongoCollection<Model> _modelCollection;
        private readonly IGridFSBucket _bucket;

        public ModelStoreClient(IOptions<ModelStoreSettings> modelStoreSettings, ILogger<ModelStoreClient> logger)
        {
            _mongoClient = new MongoClient(modelStoreSettings.Value.Uri);
            _logger = logger;
            _modelDb = _mongoClient.GetDatabase(modelStoreSettings.Value.Database);
            _modelCollection = _modelDb.GetCollection<Model>(modelStoreSettings.Value.Collection);
            _bucket = new GridFSBucket(_modelDb);
        }

        public async Task<List<string>> GetTagsAsync()
        {
            var tags = await _modelCollection.Distinct(m => m.Tag, Builders<Model>.Filter.Empty).ToListAsync();
            return tags;
        }

        public async Task<List<string>> GetMetricNamesAsync()
        {
            PipelineDefinition<Model, BsonDocument> pipeline = new BsonDocument[]
            {
                new("$project",
                new BsonDocument
                    {
                        { "_id", 0 },
                        { "metrics",
                new BsonDocument("$objectToArray", "$training_history") }
                    }),
                new BsonDocument("$group",
                new BsonDocument("_id", "$metrics.k")),
                new BsonDocument("$unwind",
                new BsonDocument("path", "$_id")),
                new BsonDocument("$project",
                new BsonDocument
                    {
                        { "metric_name", "$_id" },
                        { "_id", 0 }
                    })
            };

            var docs = await _modelCollection.Aggregate(pipeline).ToListAsync();
            var metricNames = docs.Select(d => d["metric_name"].AsString).ToList();

            return metricNames;
        }

        public async Task<List<TrainingStats>> GetTrainingStatsForTagAsync(string tag)
        {
            PipelineDefinition<Model, TrainingStats> pipeline = new BsonDocument[]
            {
                new("$match",
                new BsonDocument("tag", tag)),
                s_trainingStatsProjection
            };

            var trainingStats = await _modelCollection.Aggregate(pipeline).ToListAsync();

            return trainingStats;
        }

        public async Task<TrainingStats> GetTrainingStatsForModelAsync(ObjectId modelId)
        {
            PipelineDefinition<Model, TrainingStats> pipeline = new BsonDocument[]
            {
                new("$match",
                new BsonDocument("_id", modelId)),
                s_trainingStatsProjection
            };

            var list = await _modelCollection.Aggregate(pipeline).ToListAsync();

            TrainingStats trainingStats = null!;

            if (list.Count > 0)
                trainingStats = list[0];

            return trainingStats;
        }

        public async Task<List<TrainingData>> GetTrainingDataAsync(MetricInfo[] metricInfos)
        {
            var filter = metricInfos.Select(m => new BsonDocument
                            {
                                { "_id", m.ModelId },
                                { "metrics.k", m.MetricName }
                            });
            var ifNullFallback = metricInfos.Select(m => new BsonDocument
                                    {
                                        { "k", m.MetricName },
                                        { "v",
                                    new BsonArray() }
                                    });
            PipelineDefinition<Model, TrainingData> pipeline = new BsonDocument[]
            {
                new("$project",
                new BsonDocument
                    {
                        { "_id", 1 },
                        { "module", 1 },
                        { "class", 1 },
                        { "args", 1 },
                        { "kwargs", 1 },
                        { "metrics",
                new BsonDocument("$ifNull",
                new BsonArray
                            {
                                new BsonDocument("$objectToArray", "$training_history"),
                                new BsonArray(ifNullFallback)
                            }) }
                    }),
                new("$unwind",
                new BsonDocument
                    {
                        { "path", "$metrics" },
                        { "preserveNullAndEmptyArrays", true }
                    }),
                new("$match",
                new BsonDocument("$or",
                new BsonArray(filter))),
                new("$project",
                new BsonDocument
                    {
                        { "_id", 1 },
                        { "module", 1 },
                        { "class", 1 },
                        { "args", 1 },
                        { "kwargs", 1 },
                        { "metric_name", "$metrics.k" },
                        { "metric_history", "$metrics.v" }
                    })
            };

            var trainingData = await _modelCollection.Aggregate(pipeline).ToListAsync();

            return trainingData;
        }

        public async Task<DeleteResult> DeleteModelsAsync(ObjectId[] modelIds)
        {
            _logger.LogInformation("Deleting {modelIds}", string.Join(", ", modelIds));
            var filter = Builders<Model>.Filter.In(m => m.Id, modelIds);
            var deleteResult = await _modelCollection.DeleteManyAsync(filter);
            await Parallel.ForEachAsync(modelIds, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (modelId, ct) =>
            {
                try
                {
                    await _bucket.DeleteAsync(modelId, ct);
                }
                catch (GridFSFileNotFoundException)
                {
                    _logger.LogWarning("Model state for {modelId} not found", modelId);
                }
            });
            return deleteResult;
        }

        public async Task MonitorModelsAsync(Func<ChangeStreamDocument<Model>, CancellationToken, Task> action, CancellationToken cancellationToken)
        {
            var pipeline = new EmptyPipelineDefinition<ChangeStreamDocument<Model>>()
                .Match(change => 
                change.OperationType == ChangeStreamOperationType.Insert || 
                change.OperationType == ChangeStreamOperationType.Update || 
                change.OperationType == ChangeStreamOperationType.Delete);

            using var cursor = await _modelCollection.WatchAsync(
                pipeline, 
                new ChangeStreamOptions { FullDocument = ChangeStreamFullDocumentOption.UpdateLookup, FullDocumentBeforeChange = ChangeStreamFullDocumentBeforeChangeOption.Required }, 
                cancellationToken);

            await cursor.ForEachAsync(async change =>
            {
                _logger.LogInformation("Invoking method for update: {change}", change);
                await action(change, cancellationToken);

            }, cancellationToken);
        }

        public void Dispose()
        {
            _mongoClient?.Dispose();
        }
    }
}
