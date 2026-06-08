namespace ModelStoreApi.Dtos.Api
{
    public sealed record DeleteModelsResponse
    {
        public Dictionary<string, string> Errors { get; init; } = [];
    }
}
