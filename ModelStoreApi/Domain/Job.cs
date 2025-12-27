using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ModelStoreApi.Domain
{
    public enum JobStatus
    {
        Submitted = 0,
        Running = 1,
        Completed = 2,
        Failed = 3,
        Stopped = 4
    }

    public class Job
    {
        public ObjectId Id { get; set; }

        [BsonElement("datetime")]
        public DateTime DateTime { get; set; }

        [BsonElement("status")]
        public JobStatus Status { get; set; }

        [BsonElement("module")]
        public string Module { get; set; } = null!;

        [BsonElement("class")]
        public string Class { get; set; } = null!;

        [BsonElement("args")]
        public BsonArray Args { get; set; } = null!;

        [BsonElement("kwargs")]
        public BsonDocument KWArgs { get; set; } = null!;

        [BsonElement("error")]
        [BsonIgnoreIfNull]
        public JobError Error { get; set; } = null!;
    }
}
