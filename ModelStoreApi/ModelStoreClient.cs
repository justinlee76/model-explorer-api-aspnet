using Microsoft.Extensions.Options;
using ModelStoreApi.Domain;
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
        private readonly IMongoDatabase _db;
        private readonly IMongoCollection<Model> _models;
        private readonly IMongoCollection<Domain.Task> _tasks;
        private readonly IMongoCollection<Job> _jobs;
        private readonly IGridFSBucket _bucket;

        public ModelStoreClient(IOptions<ModelStoreSettings> modelStoreSettings, ILogger<ModelStoreClient> logger)
        {
            _mongoClient = new MongoClient(modelStoreSettings.Value.Uri);
            _logger = logger;
            _db = _mongoClient.GetDatabase(modelStoreSettings.Value.Database);
            _models = _db.GetCollection<Model>(modelStoreSettings.Value.ModelCollection);
            _tasks = _db.GetCollection<Domain.Task>(modelStoreSettings.Value.TaskCollection);
            _jobs = _db.GetCollection<Job>(modelStoreSettings.Value.JobCollection);
            _bucket = new GridFSBucket(_db);
        }

        public async Task<List<string>> GetTagsAsync()
        {
            var tags = await _models.Distinct(m => m.Tag, Builders<Model>.Filter.Empty).ToListAsync();
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
                new("$group",
                new BsonDocument("_id", "$metrics.k")),
                new("$unwind",
                new BsonDocument("path", "$_id")),
                new("$project",
                new BsonDocument
                    {
                        { "metric_name", "$_id" },
                        { "_id", 0 }
                    })
            };

            var docs = await _models.Aggregate(pipeline).ToListAsync();
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

            var trainingStats = await _models.Aggregate(pipeline).ToListAsync();

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

            var list = await _models.Aggregate(pipeline).ToListAsync();

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

            var trainingData = await _models.Aggregate(pipeline).ToListAsync();

            return trainingData;
        }

        public async Task<DeleteResult> DeleteModelsAsync(ObjectId[] modelIds)
        {
            _logger.LogInformation("Deleting {modelIds}", string.Join(", ", modelIds));
            var filter = Builders<Model>.Filter.In(m => m.Id, modelIds);
            var deleteResult = await _models.DeleteManyAsync(filter);
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

        public async System.Threading.Tasks.Task MonitorModelsAsync(Func<ChangeStreamDocument<Model>, CancellationToken, System.Threading.Tasks.Task> action, CancellationToken cancellationToken)
        {
            var pipeline = new EmptyPipelineDefinition<ChangeStreamDocument<Model>>()
                .Match(change => 
                change.OperationType == ChangeStreamOperationType.Insert || 
                change.OperationType == ChangeStreamOperationType.Update || 
                change.OperationType == ChangeStreamOperationType.Delete);

            using var cursor = await _models.WatchAsync(
                pipeline, 
                new ChangeStreamOptions { FullDocument = ChangeStreamFullDocumentOption.UpdateLookup, FullDocumentBeforeChange = ChangeStreamFullDocumentBeforeChangeOption.Required }, 
                cancellationToken);

            await cursor.ForEachAsync(async change =>
            {
                _logger.LogInformation("Invoking method for update: {change}", change);
                await action(change, cancellationToken);

            }, cancellationToken);
        }

        public async Task<List<Domain.Task>> GetTasksAsync()
        {
            var tasks = await _tasks.Find(Builders<Domain.Task>.Filter.Empty).ToListAsync();
            return tasks;
        }

        public async Task<Job> InsertJobAsync(Job job)
        {
            job.DateTime = DateTime.UtcNow;
            job.Status = JobStatus.Submitted;
            await _jobs.InsertOneAsync(job);
            return job;
        }

        public async Task<Job> GetLastJobAsync()
        {
            var sort = Builders<Job>.Sort.Descending(j => j.DateTime);
            var job = await _jobs
                .Find(Builders<Job>.Filter.Empty)
                .Sort(sort)
                .Limit(1)
                .FirstOrDefaultAsync();
            return job;
        }

        public async Task<IEnumerable<Job>> GetJobsAsync()
        {
            var sort = Builders<Job>.Sort.Descending(j => j.DateTime);
            var jobs = await _jobs
                .Find(Builders<Job>.Filter.Empty)
                .Sort(sort)
                .ToListAsync();
            return jobs;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _mongoClient?.Dispose();
            }
        }
    }
}
