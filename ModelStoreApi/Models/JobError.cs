using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ModelStoreApi.Models
{
    public class JobError
    {
        [BsonElement("exception")]
        public string Exception { get; set; } = null!;

        [BsonElement("stack_trace")]
        public string StackTrace { get; set; } = null!;
    }
}
