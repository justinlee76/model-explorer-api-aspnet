namespace ModelStoreApi.Dtos
{
    public record MetricHistoryDto(string Id, string MetricName, double[] Values);
}
