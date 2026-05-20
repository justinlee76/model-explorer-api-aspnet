using Microsoft.Extensions.Options;
using ModelStoreApi.Domain;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.IdGenerators;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;
using System.Runtime.CompilerServices;

namespace ModelStoreApi.MongoDB
{
    public class MongoModelStore : IModelStore, IDisposable
    {
        private static readonly object s_classMapLock = new();
        private static bool s_classMapsRegistered;

        private readonly MongoClient _mongoClient;
        private readonly ILogger<MongoModelStore> _logger;
        private readonly IMongoDatabase _db;
        private readonly IMongoCollection<Model> _models;
        private readonly IMongoCollection<Domain.Task> _tasks;
        private readonly IMongoCollection<Job> _jobs;
        private readonly IMongoCollection<BsonDocument> _jobsBson;
        private readonly GridFSBucket _bucket;

        public MongoModelStore(IOptions<MongoModelStoreSettings> modelStoreSettings, ILogger<MongoModelStore> logger)
        {
            RegisterDomainClassMaps();

            _mongoClient = new MongoClient(modelStoreSettings.Value.Uri);
            _logger = logger;
            _db = _mongoClient.GetDatabase(modelStoreSettings.Value.Database);
            _models = _db.GetCollection<Model>(modelStoreSettings.Value.ModelCollection);
            _tasks = _db.GetCollection<Domain.Task>(modelStoreSettings.Value.TaskCollection);
            _jobs = _db.GetCollection<Job>(modelStoreSettings.Value.JobCollection);
            _jobsBson = _db.GetCollection<BsonDocument>(modelStoreSettings.Value.JobCollection);
            _bucket = new GridFSBucket(_db);
        }

        public async Task<List<string>> GetTagsAsync()
        {
            var tags = await _models.Distinct(m => m.Tag, Builders<Model>.Filter.Empty).ToListAsync();
            return tags;
        }

        public async Task<List<string>> GetMetricNamesAsync()
        {
            var docs = await _models.Aggregate()
                .Project(new BsonDocument
                {
                    { "_id", 0 },
                    { "history", new BsonDocument("$objectToArray", "$training_history") }
                })
                .Unwind("history")
                .Group(new BsonDocument("_id", "$history.k"))
                .Project(new BsonDocument
                {
                    { "metric_name", "$_id" },
                    { "_id", 0 }
                })
                .Sort(new BsonDocument("metric_name", 1))
                .ToListAsync();

            var metricNames = docs.Select(d => d["metric_name"].AsString).ToList();

            return metricNames;
        }

        public async Task<List<Model>> GetModelsForTagAsync(string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return [];
                
            var models = await _models.Find(m => m.Tag == tag).ToListAsync();
            
            return models;
        }

        public async Task<List<MetricHistory>> GetMetricHistoryAsync(MetricHistoryKey[] metricHistoryKeys)
        {
            if (metricHistoryKeys.Length == 0)
                return [];
                
            var filters = metricHistoryKeys.Select(m => Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("_id", new ObjectId(m.ModelId)),
                Builders<BsonDocument>.Filter.Eq("history.k", m.MetricName)));

            var metricFilter = Builders<BsonDocument>.Filter.Or(filters);

            var trainingData = await _models.Aggregate()
                .Project(new BsonDocument
                {
                    { "_id", 1 },
                    { "history", new BsonDocument("$objectToArray", "$training_history") }
                })
                .Unwind("history", new AggregateUnwindOptions<BsonDocument>
                {
                    PreserveNullAndEmptyArrays = true
                })
                .Match(metricFilter)
                .Project<MetricHistory>(new BsonDocument
                {
                    { "_id", 1 },
                    { "metric_name", "$history.k" },
                    { "values", "$history.v" }
                })
                .ToListAsync();

            return trainingData;
        }

        public async Task<long> DeleteModelsAsync(string[] modelIds)
        {
            LogInformation("Deleting {ModelIds}", string.Join(", ", modelIds));

            var tasks = modelIds.Select(DeleteModelAsync);
            var deleteResults = await System.Threading.Tasks.Task.WhenAll(tasks);

            var deletedCount = deleteResults.Where(d => d.IsAcknowledged).Sum(d => d.DeletedCount);
            return deletedCount;
        }

        private async Task<DeleteResult> DeleteModelAsync(string modelId)
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
                            await _bucket.DeleteAsync(new ObjectId(modelId));
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
            await foreach (var change in MonitorCollectionAsync(_models, requireFullDocumentBeforeChange: true, cancellationToken))
            {
                var modelChange = CreateModelCollectionChange(change);
                if (modelChange != null)
                    yield return modelChange;
            }
        }

        public async IAsyncEnumerable<JobCollectionChange> MonitorJobsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var change in MonitorCollectionAsync(_jobs, requireFullDocumentBeforeChange: false, cancellationToken))
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
            var args = jobSubmission.Args.Select(ToObject).ToArray();
            var kwargs = new Dictionary<string, object>();
            foreach (var kvp in jobSubmission.KWArgs)
                kwargs.Add(kvp.Key, ToObject(kvp.Value));

            var job = new Job
            {
                TaskId = jobSubmission.TaskId,
                Args = args,
                KWArgs = kwargs,
                DateTime = DateTime.UtcNow,
                Status = JobStatus.Submitted
            };
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
            var filter = Builders<Job>.Filter.Eq(j => j.Id, jobId);
            var update = Builders<Job>.Update.Set(j => j.Status, status);
            var result = await _jobs.UpdateOneAsync(filter, update);
            return result.IsModifiedCountAvailable ? result.ModifiedCount : 0;
        }

        public async Task<long> DeleteJobAsync(string jobId)
        {
            var filter = Builders<Job>.Filter.Eq(j => j.Id, jobId);
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

        private static object ToObject(System.Text.Json.JsonElement element)
        {
            switch (element.ValueKind)
            {
                case System.Text.Json.JsonValueKind.Number:
                    if (element.TryGetInt32(out var int32Val))
                        return int32Val;
                    if (element.TryGetInt64(out var int64Val))
                        return int64Val;
                    if (element.TryGetDouble(out var doubleVal))
                        return doubleVal;
                    throw new NotSupportedException($"Conversion of this number not supported: {element.GetRawText()}");
                case System.Text.Json.JsonValueKind.True:
                case System.Text.Json.JsonValueKind.False:
                    return element.GetBoolean();
                case System.Text.Json.JsonValueKind.String:
                    return element.GetString()!;
                case System.Text.Json.JsonValueKind.Array:
                    return element.EnumerateArray().Select(ToObject).ToArray();
                case System.Text.Json.JsonValueKind.Object:
                    var doc = new Dictionary<string, object>();
                    foreach (var prop in element.EnumerateObject())
                        doc[prop.Name] = ToObject(prop.Value);
                    return doc;
                case System.Text.Json.JsonValueKind.Null:
                case System.Text.Json.JsonValueKind.Undefined:
                    return null!;
                default:
                    throw new NotSupportedException($"Unsupported value for ValueKind: {element.ValueKind}");
            }
        }

        private static void RegisterDomainClassMaps()
        {
            lock (s_classMapLock)
            {
                if (s_classMapsRegistered)
                    return;

                RegisterDomainConventions();

                var objectIdStringSerializer = new StringSerializer(BsonType.ObjectId);

                if (!BsonClassMap.IsClassMapRegistered(typeof(Model)))
                {
                    BsonClassMap.RegisterClassMap<Model>(cm =>
                    {
                        cm.AutoMap();
                        cm.SetIgnoreExtraElements(true);
                        cm.GetMemberMap(m => m.Id)
                            .SetSerializer(objectIdStringSerializer)
                            .SetIdGenerator(StringObjectIdGenerator.Instance);
                        cm.GetMemberMap(m => m.Args).SetSerializer(PlainObjectArraySerializer.Instance);
                        cm.GetMemberMap(m => m.KWArgs).SetSerializer(PlainObjectDictionarySerializer.Instance);
                    });
                }

                if (!BsonClassMap.IsClassMapRegistered(typeof(Job)))
                {
                    BsonClassMap.RegisterClassMap<Job>(cm =>
                    {
                        cm.AutoMap();
                        cm.SetIgnoreExtraElements(true);
                        cm.GetMemberMap(j => j.Id)
                            .SetSerializer(objectIdStringSerializer)
                            .SetIdGenerator(StringObjectIdGenerator.Instance);
                        cm.GetMemberMap(j => j.TaskId).SetSerializer(objectIdStringSerializer);
                        cm.GetMemberMap(j => j.Args).SetSerializer(PlainObjectArraySerializer.Instance);
                        cm.GetMemberMap(j => j.KWArgs).SetSerializer(PlainObjectDictionarySerializer.Instance);
                        cm.GetMemberMap(j => j.ModelId).SetSerializer(objectIdStringSerializer);
                        cm.GetMemberMap(j => j.Error).SetIgnoreIfNull(true);
                    });
                }

                if (!BsonClassMap.IsClassMapRegistered(typeof(Domain.Task)))
                {
                    BsonClassMap.RegisterClassMap<Domain.Task>(cm =>
                    {
                        cm.AutoMap();
                        cm.GetMemberMap(t => t.Id)
                            .SetSerializer(objectIdStringSerializer)
                            .SetIdGenerator(StringObjectIdGenerator.Instance);
                    });
                }

                if (!BsonClassMap.IsClassMapRegistered(typeof(MetricHistory)))
                {
                    BsonClassMap.RegisterClassMap<MetricHistory>(cm =>
                    {
                        cm.AutoMap();
                        cm.GetMemberMap(m => m.Id)
                            .SetSerializer(objectIdStringSerializer)
                            .SetIdGenerator(StringObjectIdGenerator.Instance);
                    });
                }

                s_classMapsRegistered = true;
            }
        }

        private static void RegisterDomainConventions()
        {
            var conventionPack = new ConventionPack
            {
                new SnakeCaseElementNameConvention()
            };

            ConventionRegistry.Register(
                "ModelStoreApi.Domain conventions",
                conventionPack,
                type => type.Namespace == typeof(Model).Namespace);
        }

        private async Task<BsonArray> GetJobMessageBsonArray(ObjectId jobId)
        {
            var filter = Builders<BsonDocument>.Filter.Eq("_id", jobId);
            var projection = Builders<BsonDocument>.Projection.Include("logs").Exclude("_id");
            var job = await _jobsBson.Find(filter).Project(projection).FirstOrDefaultAsync();
            if (job != null && job.TryGetValue("logs", out var bsonValue) && bsonValue.IsBsonArray)
                return bsonValue.AsBsonArray;
            return [];
        }

        private async IAsyncEnumerable<ChangeStreamDocument<T>> MonitorCollectionAsync<T>(
            IMongoCollection<T> collection,
            bool requireFullDocumentBeforeChange,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var pipeline = new EmptyPipelineDefinition<ChangeStreamDocument<T>>()
                .Match(change =>
                change.OperationType == ChangeStreamOperationType.Insert ||
                change.OperationType == ChangeStreamOperationType.Update ||
                change.OperationType == ChangeStreamOperationType.Delete);

            var options = new ChangeStreamOptions
            {
                FullDocument = ChangeStreamFullDocumentOption.UpdateLookup,
                FullDocumentBeforeChange = requireFullDocumentBeforeChange
                    ? ChangeStreamFullDocumentBeforeChangeOption.Required
                    : ChangeStreamFullDocumentBeforeChangeOption.Off
            };

            while (!cancellationToken.IsCancellationRequested)
            {
                IChangeStreamCursor<ChangeStreamDocument<T>> cursor;
                try
                {
                    cursor = await collection.WatchAsync(pipeline, options, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    yield break;
                }
                catch (MongoException ex)
                {
                    LogWarning("Change stream for {CollectionName} failed and will be restarted: {Message}", collection.CollectionNamespace.CollectionName, ex.Message);
                    await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                    continue;
                }

                using (cursor)
                {
                    var restart = false;
                    while (!cancellationToken.IsCancellationRequested && !restart)
                    {
                        IEnumerable<ChangeStreamDocument<T>> changes;
                        try
                        {
                            if (!await cursor.MoveNextAsync(cancellationToken))
                                break;

                            changes = cursor.Current;
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            yield break;
                        }
                        catch (MongoException ex)
                        {
                            LogWarning("Change stream for {CollectionName} failed and will be restarted: {Message}", collection.CollectionNamespace.CollectionName, ex.Message);
                            await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                            restart = true;
                            continue;
                        }

                        foreach (var change in changes)
                        {
                            LogInformation("Processing update to {CollectionName}", collection.CollectionNamespace.CollectionName);
                            yield return change;
                        }
                    }
                }
            }
        }

        private ModelCollectionChange? CreateModelCollectionChange(ChangeStreamDocument<Model> change)
        {
            LogInformation("Processing model change: {change}", change);
            if (!TryGetDocumentId(change, out var modelId))
                return null;

            var modelIdStr = modelId.ToString();
            return change.OperationType switch
            {
                ChangeStreamOperationType.Insert => new ModelCollectionChange(ModelCollectionChangeKind.Added, modelIdStr, change.FullDocument.Tag, change.FullDocument),
                ChangeStreamOperationType.Delete => new ModelCollectionChange(ModelCollectionChangeKind.Removed, modelIdStr, change.FullDocumentBeforeChange.Tag),
                ChangeStreamOperationType.Update => CreateModelUpdateCollectionChange(change, modelIdStr),
                _ => null,
            };
        }

        private static ModelCollectionChange? CreateModelUpdateCollectionChange(ChangeStreamDocument<Model> change, string modelId)
        {
            if (change.UpdateDescription == null)
                return null;

            var sendUpdate = false;
            var updatesMap = new Dictionary<MetricHistoryKey, List<MetricValueUpdate>>();
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

                if (components[0] == "training_history" || components[0] == "metrics" || components[0] == "status")
                    sendUpdate = true;
            }

            if (!sendUpdate)
                return null;

            var metricUpdates = updatesMap
                .Select(kvp => new ModelMetricUpdates(kvp.Key, kvp.Value))
                .ToList();
            return new ModelCollectionChange(
                ModelCollectionChangeKind.Updated,
                modelId,
                change.FullDocument.Tag,
                change.FullDocument,
                metricUpdates);
        }

        private static void AddMetricUpdate(Dictionary<MetricHistoryKey, List<MetricValueUpdate>> updatesMap, string modelId, string metricName, int index, double metricValue)
        {
            var key = new MetricHistoryKey(modelId, metricName);
            if (!updatesMap.TryGetValue(key, out var metricUpdates))
            {
                metricUpdates = [];
                updatesMap.Add(key, metricUpdates);
            }

            metricUpdates.Add(new MetricValueUpdate(index, metricValue));
        }

        private JobCollectionChange? CreateJobCollectionChange(ChangeStreamDocument<Job> change)
        {
            if (!TryGetDocumentId(change, out var jobId))
                return null;

            var jobIdStr = jobId.ToString();
            LogInformation("Processing job change for job {JobId}", jobIdStr);

            return change.OperationType switch
            {
                ChangeStreamOperationType.Insert => new JobCollectionChange(JobCollectionChangeKind.Added, jobIdStr, change.FullDocument),
                ChangeStreamOperationType.Update => CreateJobUpdateCollectionChange(change, jobIdStr),
                ChangeStreamOperationType.Delete => new JobCollectionChange(JobCollectionChangeKind.Removed, jobIdStr),
                _ => null,
            };
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
