using Microsoft.Extensions.Options;
using ModelStoreApi.Domain;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;
using System.Runtime.CompilerServices;

namespace ModelStoreApi
{
    public class ModelStore : IModelStore, IDisposable
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
        private readonly ILogger<ModelStore> _logger;
        private readonly IMongoDatabase _db;
        private readonly IMongoCollection<Model> _models;
        private readonly IMongoCollection<Domain.Task> _tasks;
        private readonly IMongoCollection<Job> _jobs;
        private readonly GridFSBucket _bucket;

        public ModelStore(IOptions<ModelStoreSettings> modelStoreSettings, ILogger<ModelStore> logger)
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

        public async Task<TrainingStats> GetTrainingStatsForModelAsync(string modelId)
        {
            var objectId = new ObjectId(modelId);
            PipelineDefinition<Model, TrainingStats> pipeline = new BsonDocument[]
            {
                new("$match",
                new BsonDocument("_id", objectId)),
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
                                { "_id", new ObjectId(m.ModelId) },
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

        public async Task<long> DeleteModelsAsync(string[] modelIds)
        {
            LogInformation("Deleting {ModelIds}", string.Join(", ", modelIds));

            var tasks = modelIds.Select(m => DeleteModelAsync(new ObjectId(m)));
            var deleteResults = await System.Threading.Tasks.Task.WhenAll(tasks);

            var deletedCount = deleteResults.Where(d => d.IsAcknowledged).Sum(d => d.DeletedCount);
            return deletedCount;
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

        public async IAsyncEnumerable<ModelCollectionChange> MonitorModelsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var change in MonitorCollectionAsync(_models, cancellationToken))
            {
                var modelChange = await CreateModelCollectionChangeAsync(change);
                if (modelChange != null)
                    yield return modelChange;
            }
        }

        public async IAsyncEnumerable<JobCollectionChange> MonitorJobsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var change in MonitorCollectionAsync(_jobs, cancellationToken))
            {
                var jobChange = CreateJobCollectionChange(change);
                if (jobChange != null)
                    yield return jobChange;
            }
        }

        public async Task<List<Domain.Task>> GetTasksAsync()
        {
            var tasks = await _tasks.Find(Builders<Domain.Task>.Filter.Empty).ToListAsync();
            return tasks;
        }

        public async Task<Job> InsertJobAsync(JobSubmission jobSubmission)
        {
            var args = new BsonArray(jobSubmission.Args.Select(ToBsonValue));
            var kwargs = new BsonDocument();
            foreach (var kvp in jobSubmission.KWArgs)
                kwargs.Add(kvp.Key, ToBsonValue(kvp.Value));

            var job = new Job
            {
                TaskId = new ObjectId(jobSubmission.TaskId),
                Args = args,
                KWArgs = kwargs
            };

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

        public async Task<long> UpdateJobStatusAsync(string jobId, JobStatus status)
        {
            var filter = Builders<Job>.Filter.Eq(j => j.Id, new ObjectId(jobId));
            var update = Builders<Job>.Update.Set(j => j.Status, status);
            var result = await _jobs.UpdateOneAsync(filter, update);
            return result.IsModifiedCountAvailable ? result.ModifiedCount : 0;
        }

        public async Task<long> DeleteJobAsync(string jobId)
        {
            var filter = Builders<Job>.Filter.Eq(j => j.Id, new ObjectId(jobId));
            var result = await _jobs.DeleteOneAsync(filter);
            return result.IsAcknowledged ? result.DeletedCount : 0;
        }

        public async Task<List<string>> GetJobMessagesAsync(string jobId)
        {

            var messages = new List<string>();
            var bsonArray = await GetJobMessageBsonArray(new ObjectId(jobId));
            foreach (var entry in bsonArray)
            {
                if (entry.IsBsonDocument && entry.AsBsonDocument.TryGetValue("message", out var message) && message.IsString)
                    messages.Add(message.AsString);
            }

            return messages;
        }

        private static BsonValue ToBsonValue(System.Text.Json.JsonElement element)
        {
            switch (element.ValueKind)
            {
                case System.Text.Json.JsonValueKind.Number:
                    if (element.TryGetInt32(out var int32Val))
                        return new BsonInt32(int32Val);
                    if (element.TryGetInt64(out var int64Val))
                        return new BsonInt64(int64Val);
                    if (element.TryGetDouble(out var doubleVal))
                        return new BsonDouble(doubleVal);
                    throw new NotSupportedException($"Conversion of this number not supported: {element.GetRawText()}");
                case System.Text.Json.JsonValueKind.True:
                case System.Text.Json.JsonValueKind.False:
                    return new BsonBoolean(element.GetBoolean());
                case System.Text.Json.JsonValueKind.String:
                    return new BsonString(element.GetString());
                case System.Text.Json.JsonValueKind.Array:
                    var array = new BsonArray();
                    foreach (var item in element.EnumerateArray())
                        array.Add(ToBsonValue(item));
                    return array;
                case System.Text.Json.JsonValueKind.Object:
                    var doc = new BsonDocument();
                    foreach (var prop in element.EnumerateObject())
                        doc[prop.Name] = ToBsonValue(prop.Value);
                    return doc;
                case System.Text.Json.JsonValueKind.Null:
                case System.Text.Json.JsonValueKind.Undefined:
                    return BsonNull.Value;
                default:
                    throw new NotSupportedException($"Unsupported value for ValueKind: {element.ValueKind}");
            }
        }

        private async Task<BsonArray> GetJobMessageBsonArray(ObjectId jobId)
        {
            var jobs = _db.GetCollection<BsonDocument>("jobs");
            var filter = Builders<BsonDocument>.Filter.Eq("_id", jobId);
            var projection = Builders<BsonDocument>.Projection.Include("logs").Exclude("_id");
            var job = await jobs.Find(filter).Project(projection).FirstOrDefaultAsync();
            if (job != null && job.TryGetValue("logs", out var bsonValue) && bsonValue.IsBsonArray)
                return bsonValue.AsBsonArray;
            return [];
        }

        private async IAsyncEnumerable<ChangeStreamDocument<T>> MonitorCollectionAsync<T>(
            IMongoCollection<T> collection,
            [EnumeratorCancellation] CancellationToken cancellationToken)
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

            await foreach (var change in cursor.ToAsyncEnumerable().WithCancellation(cancellationToken))
            {
                LogInformation("Processing update to {CollectionName}", collection.CollectionNamespace.CollectionName);
                yield return change;
            }
        }

        private async Task<ModelCollectionChange?> CreateModelCollectionChangeAsync(ChangeStreamDocument<Model> change)
        {
            LogInformation("Processing model change: {change}", change);
            if (!TryGetDocumentId(change, out var modelId))
                return null;

            var modelIdStr = modelId.ToString();
            switch (change.OperationType)
            {
                case ChangeStreamOperationType.Insert:
                    var insertedStats = await GetTrainingStatsForModelAsync(modelIdStr);
                    return new ModelCollectionChange(ModelCollectionChangeKind.Added, modelIdStr, change.FullDocument.Tag, insertedStats);
                case ChangeStreamOperationType.Delete:
                    return new ModelCollectionChange(ModelCollectionChangeKind.Removed, modelIdStr, change.FullDocumentBeforeChange.Tag);
                case ChangeStreamOperationType.Update:
                    return await CreateModelUpdateCollectionChangeAsync(change, modelIdStr);
                default:
                    return null;
            }
        }

        private async Task<ModelCollectionChange?> CreateModelUpdateCollectionChangeAsync(ChangeStreamDocument<Model> change, string modelId)
        {
            if (change.UpdateDescription == null)
                return null;

            var sendUpdate = false;
            var updatesMap = new Dictionary<MetricInfo, List<MetricValueUpdate>>();
            foreach (var field in change.UpdateDescription.UpdatedFields)
            {
                var components = field.Name.Split('.');
                if (components.Length == 0)
                    continue;

                if (components[0] == "training_history")
                {
                    if (components.Length == 3 && int.TryParse(components[2], out var index) && field.Value.IsDouble)
                    {
                        AddMetricUpdate(updatesMap, modelId, components[1], index, field.Value.AsDouble);
                    }
                    else if (components.Length == 1 && field.Value.IsBsonDocument)
                    {
                        foreach (var element in field.Value.AsBsonDocument.Where(e => e.Value.IsBsonArray))
                        {
                            var bsonArray = element.Value.AsBsonArray;
                            if (bsonArray.Count == 1 && bsonArray[0].IsDouble)
                                AddMetricUpdate(updatesMap, modelId, element.Name, 0, bsonArray[0].AsDouble);
                        }
                    }
                }

                if (components[0] == "training_history" || components[0] == "status")
                    sendUpdate = true;
            }

            if (!sendUpdate)
                return null;

            var trainingStats = await GetTrainingStatsForModelAsync(modelId);
            var metricUpdates = updatesMap
                .Select(kvp => new ModelMetricUpdates(kvp.Key, kvp.Value))
                .ToList();
            return new ModelCollectionChange(
                ModelCollectionChangeKind.Updated,
                modelId,
                change.FullDocument.Tag,
                trainingStats,
                metricUpdates);
        }

        private static void AddMetricUpdate(Dictionary<MetricInfo, List<MetricValueUpdate>> updatesMap, string modelId, string metricName, int index, double metricValue)
        {
            var metricInfo = new MetricInfo(modelId, metricName);
            if (!updatesMap.TryGetValue(metricInfo, out var metricUpdates))
            {
                metricUpdates = [];
                updatesMap.Add(metricInfo, metricUpdates);
            }

            metricUpdates.Add(new MetricValueUpdate(index, metricValue));
        }

        private JobCollectionChange? CreateJobCollectionChange(ChangeStreamDocument<Job> change)
        {
            if (!TryGetDocumentId(change, out var jobId))
                return null;

            var jobIdStr = jobId.ToString();
            LogInformation("Processing job change for job {JobId}", jobIdStr);

            switch (change.OperationType)
            {
                case ChangeStreamOperationType.Insert:
                    return new JobCollectionChange(JobCollectionChangeKind.Added, jobIdStr, change.FullDocument);
                case ChangeStreamOperationType.Update:
                    return CreateJobUpdateCollectionChange(change, jobIdStr);
                case ChangeStreamOperationType.Delete:
                    return new JobCollectionChange(JobCollectionChangeKind.Removed, jobIdStr);
                default:
                    return null;
            }
        }

        private JobCollectionChange? CreateJobUpdateCollectionChange(ChangeStreamDocument<Job> change, string jobId)
        {
            if (change.FullDocument == null || change.UpdateDescription == null)
                return null;

            var sendJob = false;
            var messages = new List<JobMessageChange>();
            foreach (var updatedField in change.UpdateDescription.UpdatedFields)
            {
                LogInformation("Field updated for job {JobId}: {FieldName}", jobId, updatedField.Name);
                var path = updatedField.Name.Split('.');
                if (path.Length == 0)
                    continue;

                if (path[0] == "logs")
                {
                    if (path.Length == 2)
                    {
                        if (int.TryParse(path[1], out var index) &&
                            updatedField.Value.IsBsonDocument &&
                            updatedField.Value.AsBsonDocument.TryGetValue("message", out var messageBson) &&
                            messageBson.IsString)
                        {
                            messages.Add(new JobMessageChange(jobId, index, messageBson.AsString));
                        }
                    }
                    else if (path.Length == 1 && updatedField.Value.IsBsonArray)
                    {
                        for (var i = 0; i < updatedField.Value.AsBsonArray.Count; i++)
                        {
                            var messageBson = updatedField.Value.AsBsonArray[i];
                            if (messageBson.IsBsonDocument &&
                                messageBson.AsBsonDocument.TryGetValue("message", out var messageValue) &&
                                messageValue.IsString)
                            {
                                messages.Add(new JobMessageChange(jobId, i, messageValue.AsString));
                            }
                        }
                    }
                }
                else
                {
                    sendJob = true;
                }
            }

            if (!sendJob && messages.Count == 0)
                return null;

            return new JobCollectionChange(
                JobCollectionChangeKind.Updated,
                jobId,
                sendJob ? change.FullDocument : null,
                messages);
        }

        private static bool TryGetDocumentId<T>(ChangeStreamDocument<T> change, out ObjectId documentId)
        {
            if (change.DocumentKey.TryGetValue("_id", out var bsonValue) && bsonValue.IsObjectId)
            {
                documentId = bsonValue.AsObjectId;
                return true;
            }

            documentId = default;
            return false;
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
