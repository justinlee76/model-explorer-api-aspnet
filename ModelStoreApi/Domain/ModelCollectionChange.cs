namespace ModelStoreApi.Domain
{
    public enum ModelCollectionChangeKind
    {
        Added,
        Updated,
        Removed
    }

    public record ModelMetricUpdate(string Id, string MetricName, int Index, double Value);

    public record ModelCollectionChange(
        ModelCollectionChangeKind Kind,
        string ModelId,
        string Tag,
        Model? Model = null,
        IReadOnlyList<ModelMetricUpdate>? MetricUpdates = null);
}
