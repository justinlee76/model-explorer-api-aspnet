using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelStoreApi.Dtos.Api
{
    public sealed record JobInputs
    {
        public string TaskId { get; init; } = string.Empty;

        public JsonElement[] Args { get; init; } = [];

        [JsonPropertyName("kwargs")]
        public Dictionary<string, JsonElement> Kwargs { get; init; } = [];

        public static JsonElement[] ToJsonElements(IEnumerable<object>? values)
        {
            return values?.Select(ToJsonElement).ToArray() ?? [];
        }

        public static Dictionary<string, JsonElement> ToJsonElements(IReadOnlyDictionary<string, object>? values)
        {
            return values?.ToDictionary(kvp => kvp.Key, kvp => ToJsonElement(kvp.Value)) ?? [];
        }

        private static JsonElement ToJsonElement(object? value)
        {
            return JsonSerializer.SerializeToElement(value);
        }
    }
}
