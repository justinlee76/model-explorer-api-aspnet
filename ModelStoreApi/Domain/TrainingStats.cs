namespace ModelStoreApi.Domain
{
    public class TrainingStats : ModelInfo
    {
        public int TrainableParams { get; set; }

        public double MinValLoss { get; set; }

        public double MaxValAccuracy { get; set; }
    }
}
