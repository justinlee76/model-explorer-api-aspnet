using System.Text.Json;

namespace ModelStoreApi.Dtos
{
    public record JobRequest(string Task, JsonElement[] Args, Dictionary<string, JsonElement> KWArgs);
}
