using MongoDB.Bson;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelStoreApi.JsonConverters
{
    public class BsonDocumentConverter : JsonConverter<BsonDocument>
    {
        private readonly int _decimalPlaces;

        public BsonDocumentConverter(int decimalPlaces = 6)
        {
            _decimalPlaces = decimalPlaces;
        }

        public override BsonDocument Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotImplementedException("Reading BsonDocument from JSON is not implemented");
        }

        public override void Write(Utf8JsonWriter writer, BsonDocument document, JsonSerializerOptions options)
        {
            var valueConverter = new BsonValueConverter(_decimalPlaces);
            writer.WriteStartObject();

            foreach (var element in document)
            {
                writer.WritePropertyName(element.Name);
                valueConverter.Write(writer, element.Value, options);
            }

            writer.WriteEndObject();
        }
    }
}
