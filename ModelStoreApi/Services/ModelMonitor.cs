
using Microsoft.AspNetCore.SignalR;
using ModelStoreApi.Hubs;
using MongoDB.Bson;
using MongoDB.Driver;
using ModelStoreApi.Dtos;
using ModelStoreApi.Domain;

namespace ModelStoreApi.Services
{
    public class ModelMonitor(ModelStoreClient modelStoreClient, IHubContext<ModelDataHub> hubContext, ILogger<ModelMonitor> logger) : BackgroundService
    {
        private readonly ModelStoreClient _modelStoreClient = modelStoreClient;
        private readonly IHubContext<ModelDataHub> _hubContext = hubContext;
        private readonly ILogger<ModelMonitor> _logger = logger;

        protected override async System.Threading.Tasks.Task ExecuteAsync(CancellationToken cancellationToken)
        {
            LogInformation("Starting monitoring of model collection");
            await _modelStoreClient.MonitorModelsAsync(ProcessModelChangeAsync, cancellationToken);
        }

        private async System.Threading.Tasks.Task ProcessModelChangeAsync(ChangeStreamDocument<Model> change, CancellationToken cancellationToken)
        {
            LogInformation("Processing model change: {change}", change);
            ObjectId modelId;
            if (change.DocumentKey.TryGetValue("_id", out var bsonValue) && bsonValue.IsObjectId)
            {
                modelId = bsonValue.AsObjectId;

                switch (change.OperationType)
                {
                    case ChangeStreamOperationType.Insert:
                        var trainingStats = await _modelStoreClient.GetTrainingStatsForModelAsync(modelId);
                        await SendAddTrainingStatsAsync(change.FullDocument.Tag, new TrainingStatsDto(trainingStats), cancellationToken);
                        break;
                    case ChangeStreamOperationType.Delete:
                        await SendRemoveTrainingStatsAsync(change.FullDocumentBeforeChange.Tag, modelId.ToString(), cancellationToken);
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

                            await System.Threading.Tasks.Task.WhenAll(updatesMap.Keys.Select(k => SendAddMetricDataAsync(k, updatesMap[k], cancellationToken)));

                            trainingStats = await _modelStoreClient.GetTrainingStatsForModelAsync(modelId);
                            await SendUpdateTrainingStatsAsync(change.FullDocument.Tag, new TrainingStatsDto(trainingStats), cancellationToken);
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

        private async System.Threading.Tasks.Task SendAddMetricDataAsync(SeriesKey seriesKey, List<MetricUpdate> metricUpdates, CancellationToken cancellationToken)
        {
            LogInformation("AddMetricData: seriesKey = {seriesKey}, metricUpdates = {metricUpdates}", seriesKey, metricUpdates);
            await _hubContext.Clients.Group(seriesKey.AsString).SendAsync("AddMetricData", metricUpdates, cancellationToken);
        }

        private async System.Threading.Tasks.Task SendAddTrainingStatsAsync(string tag, TrainingStatsDto trainingStats, CancellationToken cancellationToken)
        {
            LogInformation("AddTrainingStats: tag = {tag}, trainingStats = {trainingStats}", tag, trainingStats);
            await _hubContext.Clients.Group(tag).SendAsync("AddTrainingStats", trainingStats, cancellationToken);
        }

        private async System.Threading.Tasks.Task SendUpdateTrainingStatsAsync(string tag, TrainingStatsDto trainingStats, CancellationToken cancellationToken)
        {
            LogInformation("UpdateTrainingStats: tag = {tag}, trainingStats = {trainingStats}", tag, trainingStats);
            await _hubContext.Clients.Group(tag).SendAsync("UpdateTrainingStats", trainingStats, cancellationToken);
        }

        private async System.Threading.Tasks.Task SendRemoveTrainingStatsAsync(string tag, string modelId, CancellationToken cancellationToken)
        {
            LogInformation("RemoveTrainingStats: tag = {tag}, modelId = {modelId}", tag, modelId);
            await _hubContext.Clients.Group(tag).SendAsync("RemoveTrainingStats", modelId, cancellationToken);
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

    }
}
