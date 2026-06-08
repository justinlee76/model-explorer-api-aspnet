namespace ModelStoreApi.Dtos.Api
{
    public sealed record StopJobRequest
    {
        public string Id { get; init; } = string.Empty;
    }
}
