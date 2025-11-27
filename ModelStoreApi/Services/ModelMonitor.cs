
using Microsoft.AspNetCore.SignalR;
using ModelStoreApi.Models;
using ModelStoreApi.Hubs;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ModelStoreApi.Services
{
    public class ModelMonitor : BackgroundService
    {
        private readonly ModelStoreClient _modelStoreClient;
        private readonly IHubContext<ModelDataHub> _hubContext;
        private readonly ILogger<ModelMonitor> _logger;

        public ModelMonitor(ModelStoreClient modelStoreClient, IHubContext<ModelDataHub> hubContext, ILogger<ModelMonitor> logger)
        {
            _modelStoreClient = modelStoreClient;
            _hubContext = hubContext;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting monitoring of model collection");
            await _modelStoreClient.MonitorModelsAsync(ProcessModelChangeAsync, stoppingToken);
        }

        private async Task ProcessModelChangeAsync(ChangeStreamDocument<Model> change, CancellationToken stoppingToken)
        {
            _logger.LogInformation("Processing model change: {change}", change);
            ObjectId modelId;
            if (change.DocumentKey.TryGetValue("_id", out var bsonValue) && bsonValue.IsObjectId)
            {
                modelId = bsonValue.AsObjectId;

                switch (change.OperationType)
                {
                    case ChangeStreamOperationType.Insert:
                        var trainingStats = await _modelStoreClient.GetTrainingStatsForModelAsync(modelId);
                        await SendAddTrainingStatsAsync(change.FullDocument.Tag, new TrainingStatsView(trainingStats));
                        break;
                    case ChangeStreamOperationType.Delete:
                        await SendRemoveTrainingStatsAsync(change.FullDocumentBeforeChange.Tag, modelId.ToString());
                        break;
                    case ChangeStreamOperationType.Update:
                        if (change.UpdateDescription != null)
                        {
                            var updatesMap = new Dictionary<SeriesKey, List<MetricUpdate>>();
                            foreach (var field in change.UpdateDescription.UpdatedFields)
                            {

                                var components = field.Name.Split('.');
                                if (components.Length > 0 && components[0] == "training_history")
                                {
                                    var idStr = modelId.ToString();
                                    if (components.Length == 3 && int.TryParse(components[2], out var index) && field.Value.IsDouble)
                                    {
                                        var metricName = components[1];
                                        var metricValue = field.Value.AsDouble;
                                        AddMetricUpdate(updatesMap, idStr, metricName, index, metricValue);
                                    }
                                    else if (components.Length == 1 && field.Value.IsBsonDocument)
                                    {
                                        foreach (var element in field.Value.AsBsonDocument.Where(e => e.Value.IsBsonArray))
                                        {
                                            var bsonArray = element.Value.AsBsonArray;
                                            if (bsonArray.Count == 1 && bsonArray[0].IsDouble)
                                                AddMetricUpdate(updatesMap, idStr, element.Name, 0, bsonArray[0].AsDouble);
                                        }
                                    }
                                }
                            }

                            await Task.WhenAll(updatesMap.Keys.Select(k => SendAddMetricDataAsync(k, updatesMap[k])));

                            trainingStats = await _modelStoreClient.GetTrainingStatsForModelAsync(modelId);
                            await SendUpdateTrainingStatsAsync(change.FullDocument.Tag, new TrainingStatsView(trainingStats));
                        }
                        break;
                }

            }
        }

        private static void AddMetricUpdate(Dictionary<SeriesKey, List<MetricUpdate>> updatesMap, string modelId, string metricName, int index, double metricValue)
        {
            var seriesKey = new SeriesKey(modelId, metricName);
            if (!updatesMap.TryGetValue(seriesKey, out var metricUpdates))
            {
                metricUpdates = [];
                updatesMap.Add(seriesKey, metricUpdates);
            }
            metricUpdates.Add(new MetricUpdate(modelId, metricName, index, metricValue));
        }

        private async Task SendAddMetricDataAsync(SeriesKey seriesKey, List<MetricUpdate> metricUpdates)
        {
            _logger.LogInformation("AddMetricData: seriesKey = {seriesKey}, metricUpdates = {metricUpdates}", seriesKey, metricUpdates);
            await _hubContext.Clients.Group(seriesKey.AsString).SendAsync("AddMetricData", metricUpdates);
        }

        private async Task SendAddTrainingStatsAsync(string tag, TrainingStatsView trainingStatsView)
        {
            _logger.LogInformation("AddTrainingStats: tag = {tag}, trainingStatsView = {trainingStatsView}", tag, trainingStatsView);
            await _hubContext.Clients.Group(tag).SendAsync("AddTrainingStats", trainingStatsView);
        }

        private async Task SendUpdateTrainingStatsAsync(string tag, TrainingStatsView trainingStatsView)
        {
            _logger.LogInformation("UpdateTrainingStats: tag = {tag}, trainingStatsView = {trainingStatsView}", tag, trainingStatsView);
            await _hubContext.Clients.Group(tag).SendAsync("UpdateTrainingStats", trainingStatsView);
        }

        private async Task SendRemoveTrainingStatsAsync(string tag, string modelId)
        {
            _logger.LogInformation("RemoveTrainingStats: tag = {tag}, modelId = {modelId}", tag, modelId);
            await _hubContext.Clients.Group(tag).SendAsync("RemoveTrainingStats", modelId);
        }
    }
}
