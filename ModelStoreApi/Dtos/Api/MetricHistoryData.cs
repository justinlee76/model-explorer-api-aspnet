using ModelStoreApi.Domain;

namespace ModelStoreApi.Dtos.Api
{
    public sealed record MetricHistoryData
    {
        public string Id { get; init; } = string.Empty;

        public string MetricName { get; init; } = string.Empty;

        public double[] Values { get; init; } = [];

        public static MetricHistoryData FromDomain(MetricHistory history)
        {
            return new MetricHistoryData
            {
                Id = history.Id,
                MetricName = history.MetricName,
                Values = history.Values ?? []
            };
        }
    }
}
