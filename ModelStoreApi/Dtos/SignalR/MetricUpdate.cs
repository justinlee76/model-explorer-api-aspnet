using ModelStoreApi.Domain;

namespace ModelStoreApi.Dtos.SignalR
{
    public record MetricUpdate(string Id, string MetricName, int Index, double Value)
    {
        public static MetricUpdate FromDomain(ModelMetricUpdate metricUpdate) => new(metricUpdate.Id, metricUpdate.MetricName, metricUpdate.Index, metricUpdate.Value);
    }
}
