using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelStoreApi.JsonConverters
{
    public static class BsonConverter
    {
        public const int DecimalPlaces = 6;

        private static readonly JsonConverter[] s_converters =
        [
            new BsonDocumentConverter(DecimalPlaces),
            new BsonValueConverter(DecimalPlaces)
        ];

        public static string Serialize(object obj, bool writeIndented)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = writeIndented
            };

            foreach (var converter in s_converters)
                options.Converters.Add(converter);

            return JsonSerializer.Serialize(obj, options);
        }
    }
}
