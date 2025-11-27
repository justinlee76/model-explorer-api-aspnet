namespace ModelStoreApi.Services
{
    public record SeriesKey(string ModelId, string MetricName)
    {
        public string AsString => ModelId + ':' + MetricName;
    }
}
