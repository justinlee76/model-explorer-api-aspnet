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
        Stopping = 4,
        Stopped = 5,
    }

    [BsonIgnoreExtraElements]
    public class Job
    {
        public ObjectId Id { get; set; }

        [BsonElement("datetime")]
        public DateTime DateTime { get; set; }

        [BsonElement("status")]
        public JobStatus Status { get; set; }

        [BsonElement("task_id")]
        public ObjectId TaskId { get; set; }

        [BsonElement("args")]
        public BsonArray Args { get; set; } = null!;

        [BsonElement("kwargs")]
        public BsonDocument KWArgs { get; set; } = null!;

        [BsonElement("model_id")]
        public ObjectId? ModelId { get; set; }

        [BsonElement("error")]
        [BsonIgnoreIfNull]
        public JobError Error { get; set; } = null!;
    }
}
