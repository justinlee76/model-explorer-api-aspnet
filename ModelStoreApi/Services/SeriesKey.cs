namespace ModelStoreApi.Services
{
    public record SeriesKey(string ModelId, string MetricName)
    {
        public override string ToString() => ModelId + ':' + MetricName;
    }
}
