namespace ModelStoreApi.Domain
{
    public record MetricSummary(
        double? MinTrainLoss,
        double? MaxTrainAccuracy,
        double? MinValLoss,
        double? MaxValAccuracy
    );
}