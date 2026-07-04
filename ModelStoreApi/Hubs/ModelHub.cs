using ModelStoreApi.Dtos;
using ModelStoreApi.Services;

namespace ModelStoreApi.Hubs
{
    public class ModelHub(SubscriptionTracker<SeriesKey> subscriptionTracker, ILogger<ModelHub> logger) : SubscribableHub<SeriesKey>(subscriptionTracker, logger)
    {
        public async Task SubscribeAll(List<SeriesKey> seriesKeys)
        {
            await Task.WhenAll(seriesKeys.Select(Subscribe));
        }

        public async Task UnsubscribeAll(List<SeriesKey> seriesKeys)
        {
            await Task.WhenAll(seriesKeys.Select(Unsubscribe));
        }

        public async Task SubscribeTag(string tag)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, tag);
            LogInformation("Connection {connectionId} subscribed to tag {tag}", Context.ConnectionId, tag);
        }

        public async Task UnsubscribeTag(string tag)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, tag);
            LogInformation("Connection {connectionId} unsubscribed from tag {tag}", Context.ConnectionId, tag);
        }
    }
}
