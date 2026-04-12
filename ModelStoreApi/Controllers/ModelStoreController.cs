using Microsoft.AspNetCore.Mvc;
using ModelStoreApi.Domain;
using ModelStoreApi.Dtos;
using ModelStoreApi.JsonConverters;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Text.Json;

namespace ModelStoreApi.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class ModelStoreController : ControllerBase
    {
        private readonly ModelStoreClient _modelStoreClient;

        public ModelStoreController(ModelStoreClient modelStoreClient)
        {
            _modelStoreClient = modelStoreClient;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<string>>> GetTags()
        {
            var tags = await _modelStoreClient.GetTagsAsync();
            return tags;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<string>>> GetMetricNames()
        {
            var metricNames = await _modelStoreClient.GetMetricNamesAsync();
            return metricNames;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<TrainingStatsDto>>> GetTrainingStats(string tag)
        {
            var trainingStats = await _modelStoreClient.GetTrainingStatsForTagAsync(tag);
            var statViews = trainingStats.Select(s => new TrainingStatsDto(s)).ToList();
            return statViews;
        }

        [HttpPost]
        public async Task<ActionResult<IEnumerable<TrainingDataDto>>> GetTrainingData([FromBody] TrainingDataRequest request)
        {
            var metricInfos = request.SeriesKeys.Select(s => new MetricInfo(new ObjectId(s.ModelId), s.MetricName)).ToArray();
            var trainingData = await _modelStoreClient.GetTrainingDataAsync(metricInfos);
            var seriesList = trainingData.Select(d => new TrainingDataDto(d.Id.ToString(), d.MetricName, d.MetricHistory)).ToList();
            return seriesList;
        }

        [HttpPost]
        public async Task<ActionResult<DeleteResponse>> DeleteModels([FromBody] DeleteModelsRequest request)
        {
            var result = await _modelStoreClient.DeleteModelsAsync([.. request.ModelIds.Select(m => new ObjectId(m))]);
            return new DeleteResponse(result);
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<TaskDto>>> GetTasks()
        {
            var tasks = await _modelStoreClient.GetTasksAsync();
            return tasks.Select(t => new TaskDto(t)).ToList();
        }

        [HttpPost]
        public async Task<ActionResult<JobResponse>> InsertJob([FromBody] JobRequest request)
        {
            Job? job = null;
            JobErrorDto? error = null;
            try
            {
                var args = new BsonArray(request.Args.Select(ToBsonValue));
                var kwargs = new BsonDocument();
                foreach (var kvp in request.KWArgs)
                    kwargs.Add(kvp.Key, ToBsonValue(kvp.Value));
                job = await _modelStoreClient.InsertJobAsync(new Job
                {
                    TaskId = new ObjectId(request.TaskId),
                    Args = args,
                    KWArgs = kwargs
                });
            }
            catch (Exception ex)
            {
                error = new JobErrorDto(ex.Message, ex.StackTrace);
            }

            var id = job?.Id.ToString() ?? null;
            return new JobResponse(id, error);
        }

        [HttpGet]
        public async Task<ActionResult<JobDefaults>> GetJobDefaults()
        {
            var lastJob = await _modelStoreClient.GetLastJobAsync();
            string? taskId;
            string args;
            string kwargs;
            if (lastJob != null)
            {

                taskId = lastJob.TaskId.ToString();
                args = BsonConverter.Serialize(lastJob.Args, true);
                kwargs = BsonConverter.Serialize(lastJob.KWArgs, true);
            }
            else
            {
                var tasks = await _modelStoreClient.GetTasksAsync();
                taskId = tasks.FirstOrDefault()?.Id.ToString();
                args = "[]";
                kwargs = "{}";
            }

            return new JobDefaults(
                    taskId,
                    args,
                    kwargs
                );
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<JobDto>>> GetJobs()
        {
            var jobs = await _modelStoreClient.GetJobsAsync();
            var jobDtos = jobs.Select(j => new JobDto(j)).ToList();
            return jobDtos;
        }

        [HttpPost]
        public async Task<ActionResult<UpdateResponse>> StopJob([FromBody] StopJobRequest request)
        {
            var jobId = new ObjectId(request.JobId);
            var result = await _modelStoreClient.UpdateJobStatusAsync(jobId, JobStatus.Stopping);
            return new UpdateResponse(result);
        }

        [HttpDelete("{jobId}")]
        public async Task<ActionResult<DeleteResponse>> DeleteJob(string jobId)
        {
            var objId = new ObjectId(jobId);
            var result = await _modelStoreClient.DeleteJobAsync(objId);
            return new DeleteResponse(result);
        }

        [HttpGet]
        public async Task<ActionResult<GetJobMessagesResponse>> GetJobMessages(string jobId)
        {
            var objId = new ObjectId(jobId);
            var messages = await _modelStoreClient.GetJobMessagesAsync(objId);
            return new GetJobMessagesResponse([.. messages]);
        }

        private static BsonValue ToBsonValue(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Number:
                    if (element.TryGetInt32(out var int32Val))
                        return new BsonInt32(int32Val);
                    if (element.TryGetInt64(out var int64Val))
                        return new BsonInt64(int64Val);
                    if (element.TryGetDouble(out var doubleVal))
                        return new BsonDouble(doubleVal);
                    throw new NotSupportedException($"Conversion of this number not supported: {element.GetRawText()}");
                case JsonValueKind.True:
                case JsonValueKind.False:
                    return new BsonBoolean(element.GetBoolean());
                case JsonValueKind.String:
                    return new BsonString(element.GetString());
                case JsonValueKind.Array:
                    var array = new BsonArray();
                    foreach (var item in element.EnumerateArray())
                        array.Add(ToBsonValue(item));
                    return array;
                case JsonValueKind.Object:
                    var doc = new BsonDocument();
                    foreach (var prop in element.EnumerateObject())
                        doc[prop.Name] = ToBsonValue(prop.Value);
                    return doc;
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    return BsonNull.Value;
                default:
                    throw new NotSupportedException($"Unsupported value for ValueKind: {element.ValueKind}");
            }
        }
    }
}
