using System.Text.Json;

namespace ModelStoreApi.Dtos
{
    public record JobRequest(string TaskId, JsonElement[] Args, Dictionary<string, JsonElement> KWArgs);
}
