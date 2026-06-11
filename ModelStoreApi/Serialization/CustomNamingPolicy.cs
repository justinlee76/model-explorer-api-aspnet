using System.Text.Json;

namespace ModelStoreApi.Serialization
{
    public sealed class CustomNamingPolicy : JsonNamingPolicy
    {
        public override string ConvertName(string name)
        {
            return name == "DateTime"
                ? "datetime"
                : JsonNamingPolicy.CamelCase.ConvertName(name);
        }
    }
}
