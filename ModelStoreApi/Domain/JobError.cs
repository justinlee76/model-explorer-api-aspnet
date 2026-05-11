namespace ModelStoreApi.Domain
{
    public class JobError
    {
        public string Exception { get; set; } = null!;

        public string? StackTrace { get; set; } = null!;
    }
}
