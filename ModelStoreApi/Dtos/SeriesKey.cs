namespace ModelStoreApi.Dtos
{
    public sealed record SeriesKey
    {
        public string Id { get; init; } = string.Empty;

        public string MetricName { get; init; } = string.Empty;

        public override string ToString() => Id + ':' + MetricName;
    }
}
