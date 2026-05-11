using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace ModelStoreApi.MongoDB
{
    public sealed class PlainObjectArraySerializer : SerializerBase<object[]>
    {
        public static readonly PlainObjectArraySerializer Instance = new();

        public override object[] Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
        {
            var reader = context.Reader;
            if (reader.GetCurrentBsonType() == BsonType.Null)
            {
                reader.ReadNull();
                return null!;
            }

            reader.ReadStartArray();
            var values = new List<object>();
            while (reader.ReadBsonType() != BsonType.EndOfDocument)
                values.Add(PlainObjectSerializer.DeserializeValue(reader));
            reader.ReadEndArray();

            return values.ToArray();
        }

        public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, object[] value)
        {
            PlainObjectSerializer.SerializeValue(context.Writer, value);
        }
    }

    public sealed class PlainObjectDictionarySerializer : SerializerBase<Dictionary<string, object>>
    {
        public static readonly PlainObjectDictionarySerializer Instance = new();

        public override Dictionary<string, object> Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
        {
            var reader = context.Reader;
            if (reader.GetCurrentBsonType() == BsonType.Null)
            {
                reader.ReadNull();
                return null!;
            }

            return PlainObjectSerializer.DeserializeDocument(reader);
        }

        public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, Dictionary<string, object> value)
        {
            PlainObjectSerializer.SerializeValue(context.Writer, value);
        }
    }

    internal static class PlainObjectSerializer
    {
        public static object DeserializeValue(IBsonReader reader)
        {
            return reader.GetCurrentBsonType() switch
            {
                BsonType.Null => ReadNull(reader),
                BsonType.String => reader.ReadString(),
                BsonType.Boolean => reader.ReadBoolean(),
                BsonType.Int32 => reader.ReadInt32(),
                BsonType.Int64 => reader.ReadInt64(),
                BsonType.Double => reader.ReadDouble(),
                BsonType.Decimal128 => Decimal128.ToDecimal(reader.ReadDecimal128()),
                BsonType.DateTime => BsonUtils.ToDateTimeFromMillisecondsSinceEpoch(reader.ReadDateTime()),
                BsonType.ObjectId => reader.ReadObjectId().ToString(),
                BsonType.Array => DeserializeArray(reader),
                BsonType.Document => DeserializeDocumentValue(reader),
                _ => BsonSerializer.Deserialize<BsonValue>(reader)
            };
        }

        public static Dictionary<string, object> DeserializeDocument(IBsonReader reader)
        {
            reader.ReadStartDocument();
            var document = new Dictionary<string, object>();
            while (reader.ReadBsonType() != BsonType.EndOfDocument)
            {
                var name = reader.ReadName();
                document[name] = DeserializeValue(reader);
            }

            reader.ReadEndDocument();
            return document;
        }

        private static object DeserializeDocumentValue(IBsonReader reader)
        {
            var document = DeserializeDocument(reader);
            if (document.Count == 2 && document.ContainsKey("_t") && document.TryGetValue("_v", out var value))
                return value;

            return document;
        }

        public static void SerializeValue(IBsonWriter writer, object? value)
        {
            switch (value)
            {
                case null:
                    writer.WriteNull();
                    break;
                case string stringValue:
                    writer.WriteString(stringValue);
                    break;
                case bool boolValue:
                    writer.WriteBoolean(boolValue);
                    break;
                case int intValue:
                    writer.WriteInt32(intValue);
                    break;
                case long longValue:
                    writer.WriteInt64(longValue);
                    break;
                case float floatValue:
                    writer.WriteDouble(floatValue);
                    break;
                case double doubleValue:
                    writer.WriteDouble(doubleValue);
                    break;
                case decimal decimalValue:
                    writer.WriteDecimal128(decimalValue);
                    break;
                case DateTime dateTimeValue:
                    writer.WriteDateTime(BsonUtils.ToMillisecondsSinceEpoch(dateTimeValue));
                    break;
                case IEnumerable<KeyValuePair<string, object>> document:
                    SerializeDocument(writer, document);
                    break;
                case IEnumerable<object> array:
                    SerializeArray(writer, array);
                    break;
                default:
                    BsonSerializer.Serialize(writer, value.GetType(), value);
                    break;
            }
        }

        private static object[] DeserializeArray(IBsonReader reader)
        {
            reader.ReadStartArray();
            var values = new List<object>();
            while (reader.ReadBsonType() != BsonType.EndOfDocument)
                values.Add(DeserializeValue(reader));
            reader.ReadEndArray();

            return values.ToArray();
        }

        private static object ReadNull(IBsonReader reader)
        {
            reader.ReadNull();
            return null!;
        }

        private static void SerializeArray(IBsonWriter writer, IEnumerable<object> array)
        {
            writer.WriteStartArray();
            foreach (var item in array)
                SerializeValue(writer, item);
            writer.WriteEndArray();
        }

        private static void SerializeDocument(IBsonWriter writer, IEnumerable<KeyValuePair<string, object>> document)
        {
            writer.WriteStartDocument();
            foreach (var kvp in document)
            {
                writer.WriteName(kvp.Key);
                SerializeValue(writer, kvp.Value);
            }

            writer.WriteEndDocument();
        }
    }
}
