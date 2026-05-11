namespace ModelStoreApi.Domain
{
    public class TrainingData : ModelInfo
    {
        public string MetricName { get; set; } = null!;

        public double[] MetricHistory { get; set; } = null!;
    }
}
