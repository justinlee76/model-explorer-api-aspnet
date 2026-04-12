namespace ModelStoreApi.Dtos
{
    public record TaskDto(string Id, string Name)
    {
        public TaskDto(Domain.Task task) : this(task.Id.ToString(), $"{task.Module}.{task.Class}")
        {
        }
    }
}
