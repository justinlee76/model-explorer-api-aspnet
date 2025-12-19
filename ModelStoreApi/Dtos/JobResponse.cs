namespace ModelStoreApi.Dtos
{
    public class JobResponse
    {
        public string? Id { get; set; } = null!;

        public JobErrorDto? Error { get; set; } = null!;
    }
}
