using Microsoft.AspNetCore.SignalR;
using ModelStoreApi.Services;

namespace ModelStoreApi.Hubs
{
    public class SubscribableHub<T>(SubscriptionTracker<T> subscriptionTracker, ILogger logger) : Hub where T : notnull
    {
        private readonly SubscriptionTracker<T> _subscriptionTracker = subscriptionTracker;
        private readonly ILogger _logger = logger;

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var keys = _subscriptionTracker.GetKeysForConnection(Context.ConnectionId);
            await Task.WhenAll(keys.Select(Unsubscribe));
            LogInformation("Connection {ConnectionId} disconnected", Context.ConnectionId);

            await base.OnDisconnectedAsync(exception);
        }

        public async Task Subscribe(T key)
        {
            var keyString = key.ToString();
            if (keyString is null)
            {
                LogWarning("Connection {ConnectionId} attempted to subscribe with null key", Context.ConnectionId);
            }
            else
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, keyString);
                _subscriptionTracker.Subscribe(Context.ConnectionId, key);
                LogInformation("Connection {ConnectionId} subscribed to {Key}", Context.ConnectionId, key);
            }
        }

        public async Task Unsubscribe(T key)
        {
            var keyString = key.ToString();
            if (keyString is null)
            {
                LogWarning("Connection {ConnectionId} attempted to unsubscribe with null key", Context.ConnectionId);
            }
            else
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, keyString);
                _subscriptionTracker.Unsubscribe(Context.ConnectionId, key);
                LogInformation("Connection {ConnectionId} unsubscribed from {Key}", Context.ConnectionId, key);
            }
        }

        protected void LogInformation(string message, params object[] args)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
#pragma warning disable CA2254 // Template should be a static expression
                _logger.LogInformation(message, args);
#pragma warning restore CA2254 // Template should be a static expression
            }
        }

        protected void LogWarning(string message, params object[] args)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
#pragma warning disable CA2254 // Template should be a static expression
                _logger.LogWarning(message, args);
#pragma warning restore CA2254 // Template should be a static expression
            }
        }
    }
}
