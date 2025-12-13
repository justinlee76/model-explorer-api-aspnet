using System.Text.Json;

namespace ModelStoreApi.Dtos
{
    public class JobRequest
    {
        public string Task { get; set; } = null!;
        public JsonElement[] Args { get; set; } = null!;
        public Dictionary<string, JsonElement> KWArgs { get; set; } = null!;
    }
}
