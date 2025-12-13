using ModelStoreApi.Domain;

namespace ModelStoreApi.Dtos
{
    public class JobResponse
    {
        public string? Id { get; set; } = null!;

        public JobError? Error { get; set; } = null!;
    }
}
