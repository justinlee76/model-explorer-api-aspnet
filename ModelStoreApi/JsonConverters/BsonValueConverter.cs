using System.Text.Json;
using System.Text.Json.Serialization;
using MongoDB.Bson;

namespace ModelStoreApi.JsonConverters
{
    public class BsonValueConverter : JsonConverter<BsonValue>
    {
        private readonly int _decimalPlaces;

        public BsonValueConverter(int decimalPlaces = 6)
        {
            _decimalPlaces = decimalPlaces;
        }

        public override BsonValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }

        public override void Write(Utf8JsonWriter writer, BsonValue value, JsonSerializerOptions options)
        {
            if (value == null || value.IsBsonNull)
            {
                writer.WriteNullValue();
                return;
            }

            switch (value.BsonType)
            {
                case BsonType.Double:
                    double doubleValue = value.AsDouble;
                    double rounded = Math.Round(doubleValue, _decimalPlaces);
                    writer.WriteNumberValue(rounded);
                    break;

                case BsonType.Int32:
                    writer.WriteNumberValue(value.AsInt32);
                    break;

                case BsonType.Int64:
                    writer.WriteNumberValue(value.AsInt64);
                    break;

                case BsonType.Decimal128:
                    writer.WriteNumberValue(decimal.Parse(value.AsDecimal128.ToString()));
                    break;

                case BsonType.String:
                    writer.WriteStringValue(value.AsString);
                    break;

                case BsonType.Boolean:
                    writer.WriteBooleanValue(value.AsBoolean);
                    break;

                case BsonType.DateTime:
                    writer.WriteStringValue(value.ToUniversalTime().ToString("o"));
                    break;

                case BsonType.ObjectId:
                    writer.WriteStringValue(value.AsObjectId.ToString());
                    break;

                case BsonType.Array:
                    writer.WriteStartArray();
                    foreach (var item in value.AsBsonArray)
                    {
                        Write(writer, item, options);
                    }
                    writer.WriteEndArray();
                    break;

                case BsonType.Document:
                    writer.WriteStartObject();
                    foreach (var element in value.AsBsonDocument)
                    {
                        writer.WritePropertyName(element.Name);
                        Write(writer, element.Value, options);
                    }
                    writer.WriteEndObject();
                    break;

                default:
                    writer.WriteStringValue(value.ToString());
                    break;
            }
        }
    }
}
