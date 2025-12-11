using Microsoft.AspNetCore.Mvc;
using ModelStoreApi.Models;
using ModelStoreApi.Services;
using MongoDB.Bson;
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

        [HttpPost]
        public async Task<ActionResult<IEnumerable<string>>> GetTags()
        {
            var tags = await _modelStoreClient.GetTagsAsync();
            return tags;
        }

        [HttpPost]
        public async Task<ActionResult<IEnumerable<string>>> GetMetricNames()
        {
            var metricNames = await _modelStoreClient.GetMetricNamesAsync();
            return metricNames;
        }

        [HttpPost]
        public async Task<ActionResult<IEnumerable<TrainingStatsView>>> GetTrainingStats([FromBody] string tag)
        {
            var trainingStats = await _modelStoreClient.GetTrainingStatsForTagAsync(tag);
            var statViews = trainingStats.Select(s => new TrainingStatsView(s)).ToList();
            return statViews;
        }

        [HttpPost]
        public async Task<ActionResult<IEnumerable<TrainingDataView>>> GetTrainingData([FromBody] SeriesKey[] seriesKeys)
        {
            var metricInfos = seriesKeys.Select(s => new MetricInfo(new ObjectId(s.ModelId), s.MetricName)).ToArray();
            var trainingData = await _modelStoreClient.GetTrainingDataAsync(metricInfos);
            var seriesList = trainingData.Select(d => new TrainingDataView(d.Id.ToString(), d.MetricName, d.MetricHistory)).ToList();
            return seriesList;
        }

        [HttpPost]
        public async Task<ActionResult<long>> DeleteModels([FromBody] string[] modelIds)
        {
            var result = await _modelStoreClient.DeleteModelsAsync([.. modelIds.Select(m => new ObjectId(m))]);
            return result.DeletedCount;
        }

        [HttpPost]
        public async Task<ActionResult<IEnumerable<Models.Task>>> GetTasks()
        {
            var tasks = await _modelStoreClient.GetTasksAsync();
            return tasks;
        }

        [HttpPost]
        public async Task<ActionResult<string>> InsertJob([FromBody] JobRequest request)
        {
            var index = request.Task.LastIndexOf('.');
            var taskClass = request.Task[(index + 1)..];
            var taskModule = request.Task[..index];
            var args = new BsonArray(request.Args.Select(ToBsonValue));
            var kwargs = new BsonDocument();
            foreach (var kvp in request.KWArgs)
                kwargs.Add(kvp.Key, ToBsonValue(kvp.Value));
            var job = await _modelStoreClient.InsertJobAsync(new Job 
            {
                Module = taskModule,
                Class = taskClass,
                Args = args,
                KWArgs = kwargs
            });
            return job.Id.ToString();
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
