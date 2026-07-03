namespace ModelStoreApi.Services
{
    public class SubscriptionTracker<T> where T : notnull
    {
        private readonly Lock _lock = new();
        private readonly Dictionary<T, HashSet<string>> _keysToConnections = new();
        private readonly Dictionary<string, HashSet<T>> _connectionsToKeys = new();

        public void Subscribe(string connectionId, T key)
        {
            using (_lock.EnterScope())
            {
                HashSet<string> ids;
                if (!_keysToConnections.TryGetValue(key, out ids!))
                {
                    ids = [];
                    _keysToConnections.Add(key, ids);
                }
                ids.Add(connectionId);

                HashSet<T> keys;
                if (!_connectionsToKeys.TryGetValue(connectionId, out keys!))
                {
                    keys = [];
                    _connectionsToKeys.Add(connectionId, keys);
                }
                keys.Add(key);
            }
        }

        public void Unsubscribe(string connectionId, T key)
        {
            using (_lock.EnterScope())
            {
                HashSet<string> ids;
                if (_keysToConnections.TryGetValue(key, out ids!))
                {
                    ids.Remove(connectionId);

                    if (ids.Count == 0)
                        _keysToConnections.Remove(key);
                }

                HashSet<T> keys;
                if (_connectionsToKeys.TryGetValue(connectionId,out keys!))
                {
                    keys.Remove(key);

                    if (keys.Count == 0)
                        _connectionsToKeys.Remove(connectionId);
                }
            }
        }

        public IReadOnlyCollection<T> GetKeysForConnection(string connectionId)
        {
            using (_lock.EnterScope())
            {
                return _connectionsToKeys.TryGetValue(connectionId, out var keys)
                    ? keys.ToArray()
                    : [];
            }
        }
    }
}
