using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ModelStoreApi.Domain
{
    public enum ModelStatus
    {
        Training = 0,
        Trained = 1,
    }

    public class ModelInfo
    {
        [BsonElement("datetime")]
        public DateTime DateTime { get; set; }

        public ObjectId Id { get; set; }  
        
        [BsonElement("class")]
        public string Class { get; set; } = null!;

        [BsonElement("module")]
        public string Module { get; set; } = null!;

        [BsonElement("args")]
        public object[] Args { get; set; } = null!;

        [BsonElement("kwargs")]
        public Dictionary<string, object> KWArgs { get; set; } = null!;

        [BsonElement("tag")]
        public string Tag { get; set; } = null!;

        [BsonElement("status")]
        public ModelStatus Status { get; set; }
    }
}
