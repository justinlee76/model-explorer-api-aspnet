using Microsoft.AspNetCore.Mvc;
using ModelStoreApi.Models;
using ModelStoreApi.Services;
using MongoDB.Bson;

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
    }
}
