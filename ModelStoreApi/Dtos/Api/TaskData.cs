namespace ModelStoreApi.Dtos.Api
{
    public sealed record TaskData
    {
        public string Id { get; init; } = string.Empty;

        public string FullClassName { get; init; } = string.Empty;

        public static TaskData FromDomain(Domain.Task task)
        {
            return new TaskData
            {
                Id = task.Id,
                FullClassName = $"{task.Module}.{task.Class}"
            };
        }
    }
}
