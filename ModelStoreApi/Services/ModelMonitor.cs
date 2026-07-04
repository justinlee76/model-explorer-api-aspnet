
using Microsoft.AspNetCore.SignalR;
using ModelStoreApi.Hubs;
using ModelStoreApi.Dtos;
using ModelStoreApi.Dtos.SignalR;
using ModelStoreApi.Domain;

namespace ModelStoreApi.Services
{
    public class ModelMonitor(IModelStore modelStore, IHubContext<ModelHub> hubContext, ILogger<ModelMonitor> logger) : BackgroundService
    {
        private readonly IModelStore _modelStore = modelStore;
        private readonly IHubContext<ModelHub> _hubContext = hubContext;
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
                            await SendAddModelAsync(change.Tag, ModelData.FromDomain(change.Model), cancellationToken);
                        break;
                    case ModelCollectionChangeKind.Removed:
                        await SendRemoveModelAsync(change.Tag, change.ModelId, cancellationToken);
                        break;
                    case ModelCollectionChangeKind.Updated:
                        if (change.MetricUpdates != null)
                            await System.Threading.Tasks.Task.WhenAll(change.MetricUpdates.Select(m => SendAddMetricDataAsync(m, cancellationToken)));
                        if (change.Model != null)
                            await SendUpdateModelAsync(change.Tag, ModelData.FromDomain(change.Model), cancellationToken);
                        break;
                }
            }
        }

        private async System.Threading.Tasks.Task SendAddMetricDataAsync(ModelMetricUpdate modelMetricUpdate, CancellationToken cancellationToken)
        {
            var seriesKey = new SeriesKey { Id = modelMetricUpdate.Id, MetricName = modelMetricUpdate.MetricName };
            var metricUpdate = MetricUpdate.FromDomain(modelMetricUpdate);
            LogInformation("AddMetricData: seriesKey = {seriesKey}, metricUpdate = {metricUpdate}", seriesKey, metricUpdate);
            await _hubContext.Clients.Group(seriesKey.ToString()).SendAsync("AddMetricData", metricUpdate, cancellationToken);
        }

        private async System.Threading.Tasks.Task SendAddModelAsync(string tag, ModelData model, CancellationToken cancellationToken)
        {
            LogInformation("AddModel: tag = {tag}, model = {model}", tag, model);
            await _hubContext.Clients.Group(tag).SendAsync("AddModel", model, cancellationToken);
        }

        private async System.Threading.Tasks.Task SendUpdateModelAsync(string tag, ModelData model, CancellationToken cancellationToken)
        {
            LogInformation("UpdateModel: tag = {tag}, model = {model}", tag, model);
            await _hubContext.Clients.Group(tag).SendAsync("UpdateModel", model, cancellationToken);
        }

        private async System.Threading.Tasks.Task SendRemoveModelAsync(string tag, string modelId, CancellationToken cancellationToken)
        {
            LogInformation("RemoveModel: tag = {tag}, modelId = {modelId}", tag, modelId);
            await _hubContext.Clients.Group(tag).SendAsync("RemoveModel", modelId, cancellationToken);
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
