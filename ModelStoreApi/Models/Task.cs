using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ModelStoreApi.Models
{
    public class Task
    {
        public ObjectId Id { get; set; }

        [BsonElement("module")]
        public string Module { get; set; } = null!;

        [BsonElement("class")]
        public string Class { get; set; } = null!;
    }
}
