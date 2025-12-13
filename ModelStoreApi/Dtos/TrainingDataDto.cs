namespace ModelStoreApi.Dtos
{
    public record TrainingDataDto(string Id, string MetricName, double[] MetricHistory);
}
