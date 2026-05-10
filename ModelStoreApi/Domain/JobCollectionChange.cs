namespace ModelStoreApi.Domain
{
    public enum JobCollectionChangeKind
    {
        Added,
        Updated,
        Removed
    }

    public record JobMessageChange(string JobId, int Index, string Message);

    public record JobCollectionChange(
        JobCollectionChangeKind Kind,
        string JobId,
        Job? Job = null,
        IReadOnlyList<JobMessageChange>? Messages = null);
}
