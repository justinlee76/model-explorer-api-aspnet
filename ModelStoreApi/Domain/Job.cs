namespace ModelStoreApi.Domain
{
    public enum JobStatus
    {
        Submitted = 0,
        Running = 1,
        Completed = 2,
        Failed = 3,
        Stopping = 4,
        Stopped = 5,
    }

    public class Job
    {
        public string Id { get; set; } = null!;

        public DateTime DateTime { get; set; }

        public JobStatus Status { get; set; }

        public string TaskId { get; set; } = null!;

        public object[] Args { get; set; } = null!;

        public Dictionary<string, object> KWArgs { get; set; } = null!;

        public string? ModelId { get; set; }

        public JobError Error { get; set; } = null!;
    }
}
