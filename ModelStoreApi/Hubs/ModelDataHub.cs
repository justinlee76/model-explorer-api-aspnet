using Microsoft.AspNetCore.SignalR;
using ModelStoreApi.Services;

namespace ModelStoreApi.Hubs
{
    public class ModelDataHub(SubscriptionTracker<SeriesKey> subscriptionTracker, ILogger<ModelDataHub> logger) : Hub
    {
        private readonly SubscriptionTracker<SeriesKey> _subscriptionTracker = subscriptionTracker;
        private readonly ILogger<ModelDataHub> _logger = logger;

        public async Task Subscribe(List<SeriesKey> seriesKeys) 
        {
            await Task.WhenAll(seriesKeys.Select(SubscribeToSeries));
        }

        public async Task Unsubscribe(List<SeriesKey> seriesKeys)
        {
            await Task.WhenAll(seriesKeys.Select(UnsubscribeFromSeries));
        }

        public async Task MonitorTag(string tag)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, tag);
            LogInformation("Connection {connectionId} monitoring tag {tag}", Context.ConnectionId, tag);
        }

        public async Task EndMonitoring(string tag)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, tag);
            LogInformation("Connection {connectionId} ended monitoring of tag {tag}", Context.ConnectionId, tag);
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var seriesKeys = _subscriptionTracker.GetSeriesForConnection(Context.ConnectionId);
            await Task.WhenAll(seriesKeys.Select(UnsubscribeFromSeries));
            LogInformation("Connection {connectionId} disconnected", Context.ConnectionId);

            await base.OnDisconnectedAsync(exception);
        }

        private async Task SubscribeToSeries(SeriesKey seriesKey)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, seriesKey.AsString);
            _subscriptionTracker.Subscribe(Context.ConnectionId, seriesKey);
            LogInformation("Connection {connectionId} subscribed to {seriesKey}", Context.ConnectionId, seriesKey);
        }

        private async Task UnsubscribeFromSeries(SeriesKey seriesKey)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, seriesKey.AsString);
            _subscriptionTracker.Unsubscribe(Context.ConnectionId, seriesKey);
            LogInformation("Connection {connectionId} unsubscribed from {seriesKey}", Context.ConnectionId, seriesKey);
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
