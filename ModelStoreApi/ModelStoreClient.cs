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
        private readonly GridFSBucket _bucket;

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
            var metricNames = metricInfos.Select(m => m.MetricName).Distinct();
            var ifNullFallback = metricNames.Select(m => new BsonDocument
                                    {
                                        { "k", m },
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
            LogInformation("Deleting {ModelIds}", string.Join(", ", modelIds));

            var tasks = modelIds.Select(DeleteModelAsync);
            var deleteResults = await System.Threading.Tasks.Task.WhenAll(tasks);

            var deletedCount = deleteResults.Where(d => d.IsAcknowledged).Sum(d => d.DeletedCount);
            var deleteResult = new DeleteResult.Acknowledged(deletedCount);

            return deleteResult;
        }

        private async Task<DeleteResult> DeleteModelAsync(ObjectId modelId)
        {
            LogInformation("Deleting {ModelId}", modelId);

            var filter = Builders<Model>.Filter.Eq(m => m.Id, modelId);
            var model = await _models.Find(filter).FirstOrDefaultAsync();

            DeleteResult deleteResult = null!;
            if (model != null)
            {
                if (model.Status == ModelStatus.Trained)
                {
                    deleteResult = await _models.DeleteOneAsync(filter);

                    if (deleteResult.IsAcknowledged && deleteResult.DeletedCount == 1)
                    {
                        try
                        {
                            await _bucket.DeleteAsync(modelId);
                        }
                        catch (GridFSFileNotFoundException)
                        {
                            LogWarning("Model state for {ModelId} not found", modelId);
                        }
                    }
                } else
                    LogInformation("Model {ModelId} not deleted because it is still being trained", modelId);
            } else
                LogInformation("Model {ModelId} not found", modelId);

            deleteResult ??= new DeleteResult.Acknowledged(0);

            return deleteResult;
        }

        public async System.Threading.Tasks.Task MonitorModelsAsync(Func<ChangeStreamDocument<Model>, CancellationToken, System.Threading.Tasks.Task> action, CancellationToken cancellationToken)
        {
            await MonitorCollectionAsync(_models, action, cancellationToken);
        }

        public async System.Threading.Tasks.Task MonitorJobsAsync(Func<ChangeStreamDocument<Job>, CancellationToken, System.Threading.Tasks.Task> action, CancellationToken cancellationToken)
        {
            await MonitorCollectionAsync(_jobs, action, cancellationToken);
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

        public async Task<UpdateResult> UpdateJobStatusAsync(ObjectId jobId, JobStatus status)
        {
            var filter = Builders<Job>.Filter.Eq(j => j.Id, jobId);
            var update = Builders<Job>.Update.Set(j => j.Status, status);
            return await _jobs.UpdateOneAsync(filter, update);
        }

        public async Task<DeleteResult> DeleteJobAsync(ObjectId jobId)
        {
            var filter = Builders<Job>.Filter.Eq(j => j.Id, jobId);
            return await _jobs.DeleteOneAsync(filter);
        }

        public async Task<List<string>> GetJobMessagesAsync(ObjectId jobId)
        {

            var messages = new List<string>();
            var bsonArray = await GetJobMessageBsonArray(jobId);
            foreach (var entry in bsonArray)
            {
                if (entry.IsBsonDocument && entry.AsBsonDocument.TryGetValue("message", out var message) && message.IsString)
                    messages.Add(message.AsString);
            }

            return messages;
        }

        public async Task<string> GetJobMessageAtIndexAsync(ObjectId jobId, int index)
        {
            var bsonArray = await GetJobMessageBsonArray(jobId);
            if (index >= 0 && index < bsonArray.Count)
            {
                var entry = bsonArray[index];
                if (entry.IsBsonDocument && entry.AsBsonDocument.TryGetValue("message", out var message) && message.IsString)
                    return message.AsString;
            }
            return string.Empty;
        }

        private async Task<BsonArray> GetJobMessageBsonArray(ObjectId jobId)
        {
            var jobs = _db.GetCollection<BsonDocument>("jobs");
            var filter = Builders<BsonDocument>.Filter.Eq("_id", jobId);
            var job = await jobs.Find(filter).FirstOrDefaultAsync();
            if (job != null && job.TryGetValue("logs", out var bsonValue) && bsonValue.IsBsonArray)
                return bsonValue.AsBsonArray;
            return [];
        }

        private async System.Threading.Tasks.Task MonitorCollectionAsync<T>(IMongoCollection<T> collection, Func<ChangeStreamDocument<T>, CancellationToken, System.Threading.Tasks.Task> action, CancellationToken cancellationToken)
        {
            var pipeline = new EmptyPipelineDefinition<ChangeStreamDocument<T>>()
                .Match(change =>
                change.OperationType == ChangeStreamOperationType.Insert ||
                change.OperationType == ChangeStreamOperationType.Update ||
                change.OperationType == ChangeStreamOperationType.Delete);

            using var cursor = await collection.WatchAsync(
                pipeline,
                new ChangeStreamOptions { FullDocument = ChangeStreamFullDocumentOption.UpdateLookup, FullDocumentBeforeChange = ChangeStreamFullDocumentBeforeChangeOption.Required },
                cancellationToken);

            await cursor.ForEachAsync(async change =>
            {
                LogInformation("Invoking method for update to {CollectionName}", collection.CollectionNamespace.CollectionName);
                await action(change, cancellationToken);

            }, cancellationToken);
        }

        private void LogInformation(string message, params object[] args)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
#pragma warning disable CA2254 // Template should be a static expression
                _logger.LogInformation(message, args);
#pragma warning restore CA2254 // Template should be a static expression
            }
        }

        private void LogWarning(string message, params object[] args)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
#pragma warning disable CA2254 // Template should be a static expression
                _logger.LogWarning(message, args);
#pragma warning restore CA2254 // Template should be a static expression
            }
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
