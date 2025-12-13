using MongoDB.Bson.Serialization.Attributes;

namespace ModelStoreApi.Domain
{
    public class TrainingStats : ModelInfo
    {
        [BsonElement("trainable_params")]
        public int TrainableParams { get; set; }

        [BsonElement("min_val_loss")]
        public double MinValLoss { get; set; }

        [BsonElement("max_val_accuracy")]
        public double MaxValAccuracy { get; set; }
    }
}
