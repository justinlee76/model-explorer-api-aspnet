namespace ModelStoreApi.Domain
{
    public enum ModelCollectionChangeKind
    {
        Added,
        Updated,
        Removed
    }

    public record MetricValueUpdate(int Index, double Value);

    public record ModelMetricUpdates(MetricHistoryKey Metric, IReadOnlyList<MetricValueUpdate> Updates);

    public record ModelCollectionChange(
        ModelCollectionChangeKind Kind,
        string ModelId,
        string Tag,
        Model? Model = null,
        IReadOnlyList<ModelMetricUpdates>? MetricUpdates = null);
}
