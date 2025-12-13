using MongoDB.Bson.Serialization.Attributes;

namespace ModelStoreApi.Domain
{
    public class TrainingData : ModelInfo
    {
        [BsonElement("metric_name")]
        public string MetricName { get; set; } = null!;

        [BsonElement("metric_history")]
        public double[] MetricHistory { get; set; } = null!;
    }
}
