
using Microsoft.AspNetCore.SignalR;
using ModelStoreApi.Hubs;
using ModelStoreApi.Dtos;
using ModelStoreApi.Domain;

namespace ModelStoreApi.Services
{
    public class ModelMonitor(IModelStore modelStore, IHubContext<ModelDataHub> hubContext, ILogger<ModelMonitor> logger) : BackgroundService
    {
        private readonly IModelStore _modelStore = modelStore;
        private readonly IHubContext<ModelDataHub> _hubContext = hubContext;
        private readonly ILogger<ModelMonitor> _logger = logger;

        protected override async System.Threading.Tasks.Task ExecuteAsync(CancellationToken cancellationToken)
        {
            LogInformation("Starting monitoring of model collection");
            await foreach (var change in _modelStore.MonitorModelsAsync(cancellationToken))
            {
                switch (change.Kind)
                {
                    case ModelCollectionChangeKind.Added:
                        if (change.Model != null)
                            await SendAddTrainingStatsAsync(change.Tag, new ModelDto(change.Model), cancellationToken);
                        break;
                    case ModelCollectionChangeKind.Removed:
                        await SendRemoveTrainingStatsAsync(change.Tag, change.ModelId, cancellationToken);
                        break;
                    case ModelCollectionChangeKind.Updated:
                        if (change.MetricUpdates != null)
                            await System.Threading.Tasks.Task.WhenAll(change.MetricUpdates.Select(m => SendAddMetricDataAsync(m, cancellationToken)));
                        if (change.Model != null)
                            await SendUpdateTrainingStatsAsync(change.Tag, new ModelDto(change.Model), cancellationToken);
                        break;
                }
            }
        }

        private async System.Threading.Tasks.Task SendAddMetricDataAsync(ModelMetricUpdates metricUpdates, CancellationToken cancellationToken)
        {
            var seriesKey = new SeriesKey(metricUpdates.Metric.ModelId, metricUpdates.Metric.MetricName);
            var updates = metricUpdates.Updates
                .Select(u => new MetricUpdate(metricUpdates.Metric.ModelId, metricUpdates.Metric.MetricName, u.Index, u.Value))
                .ToList();
            LogInformation("AddMetricData: seriesKey = {seriesKey}, metricUpdates = {metricUpdates}", seriesKey, metricUpdates);
            await _hubContext.Clients.Group(seriesKey.ToString()).SendAsync("AddMetricData", updates, cancellationToken);
        }

        private async System.Threading.Tasks.Task SendAddTrainingStatsAsync(string tag, ModelDto trainingStats, CancellationToken cancellationToken)
        {
            LogInformation("AddTrainingStats: tag = {tag}, trainingStats = {trainingStats}", tag, trainingStats);
            await _hubContext.Clients.Group(tag).SendAsync("AddTrainingStats", trainingStats, cancellationToken);
        }

        private async System.Threading.Tasks.Task SendUpdateTrainingStatsAsync(string tag, ModelDto trainingStats, CancellationToken cancellationToken)
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
