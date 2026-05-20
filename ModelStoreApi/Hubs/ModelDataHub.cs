using ModelStoreApi.Dtos;
using ModelStoreApi.Services;

namespace ModelStoreApi.Hubs
{
    public class ModelDataHub(SubscriptionTracker<SeriesKey> subscriptionTracker, ILogger<ModelDataHub> logger) : SubscribableHub<SeriesKey>(subscriptionTracker, logger)
    {
        public async Task SubscribeAll(List<SeriesKey> seriesKeys)
        {
            await Task.WhenAll(seriesKeys.Select(Subscribe));
        }

        public async Task UnsubscribeAll(List<SeriesKey> seriesKeys)
        {
            await Task.WhenAll(seriesKeys.Select(Unsubscribe));
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
    }
}
