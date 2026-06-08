using ModelStoreApi.Domain;

namespace ModelStoreApi.Dtos
{
    public sealed record JobData
    {
        public string Id { get; init; } = string.Empty;

        public DateTime DateTime { get; init; }

        public string TaskId { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;

        public object[] Args { get; init; } = [];

        public Dictionary<string, object> Kwargs { get; init; } = [];

        public string? ModelId { get; init; }

        public static JobData FromDomain(Job job)
        {
            return new JobData
            {
                Id = job.Id,
                DateTime = job.DateTime,
                TaskId = job.TaskId,
                Status = job.Status.ToString(),
                Args = job.Args ?? [],
                Kwargs = job.KWArgs ?? [],
                ModelId = job.ModelId
            };
        }
    }
}
