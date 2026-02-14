using ModelStoreApi.Services;

namespace ModelStoreApi.Hubs
{
    public class JobHub(SubscriptionTracker<string> subscriptionTracker, ILogger<JobHub> logger) : SubscribableHub<string>(subscriptionTracker, logger)
    {
    }
}
