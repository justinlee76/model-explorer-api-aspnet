namespace ModelStoreApi.Domain
{
    public class Model: ModelInfo
    {
        public string Desc { get; set; } = null!;

        public int TrainableParams { get; set; }

        public int NonTrainableParams { get; set; }

        public int TotalParams {  get; set; }

        public Dictionary<string, double[]> TrainingHistory { get; set; } = null!;
    }
}
