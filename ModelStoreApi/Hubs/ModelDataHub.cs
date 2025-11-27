using Microsoft.AspNetCore.SignalR;
using ModelStoreApi.Services;

namespace ModelStoreApi.Hubs
{
    public class ModelDataHub : Hub
    {
        private readonly SubscriptionTracker<SeriesKey> _subscriptionTracker;
        private readonly ILogger<ModelDataHub> _logger;
        public ModelDataHub(SubscriptionTracker<SeriesKey> subscriptionTracker, ILogger<ModelDataHub> logger)
        {
            _subscriptionTracker = subscriptionTracker;
            _logger = logger;
        }

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
            _logger.LogInformation("Connection {connectionId} monitoring tag {tag}", Context.ConnectionId, tag);
        }

        public async Task EndMonitoring(string tag)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, tag);
            _logger.LogInformation("Connection {connectionId} ended monitoring of tag {tag}", Context.ConnectionId, tag);
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var seriesKeys = _subscriptionTracker.GetSeriesForConnection(Context.ConnectionId);
            await Task.WhenAll(seriesKeys.Select(UnsubscribeFromSeries));
            _logger.LogInformation("Connection {connectionId} disconnected", Context.ConnectionId);

            await base.OnDisconnectedAsync(exception);
        }

        private async Task SubscribeToSeries(SeriesKey seriesKey)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, seriesKey.AsString);
            _subscriptionTracker.Subscribe(Context.ConnectionId, seriesKey);
            _logger.LogInformation("Connection {connectionId} subscribed to {seriesKey}", Context.ConnectionId, seriesKey);
        }

        private async Task UnsubscribeFromSeries(SeriesKey seriesKey)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, seriesKey.AsString);
            _subscriptionTracker.Unsubscribe(Context.ConnectionId, seriesKey);
            _logger.LogInformation("Connection {connectionId} unsubscribed from {seriesKey}", Context.ConnectionId, seriesKey);
        }
    }
}
