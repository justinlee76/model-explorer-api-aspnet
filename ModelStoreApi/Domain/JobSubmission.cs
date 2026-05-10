using System.Text.Json;

namespace ModelStoreApi.Domain
{
    public record JobSubmission(string TaskId, JsonElement[] Args, Dictionary<string, JsonElement> KWArgs);
}
