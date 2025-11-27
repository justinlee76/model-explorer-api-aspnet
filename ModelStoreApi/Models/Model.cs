using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ModelStoreApi.Models
{
    [BsonIgnoreExtraElements]
    public class Model: ModelInfo
    {
        [BsonElement("desc")]
        public string Desc { get; set; } = null!;

        [BsonElement("trainable_params")]
        public int TrainableParams { get; set; }

        [BsonElement("nontrainable_params")]
        public int NonTrainableParams { get; set; }

        [BsonElement("total_params")]
        public int TotalParams {  get; set; }

        [BsonElement("training_history")]
        public Dictionary<string, double[]> TrainingHistory { get; set; } = null!;
    }
}
