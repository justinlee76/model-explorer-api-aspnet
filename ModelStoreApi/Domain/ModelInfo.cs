namespace ModelStoreApi.Domain
{
    public enum ModelStatus
    {
        Training = 0,
        Trained = 1,
    }

    public class ModelInfo
    {
        public DateTime DateTime { get; set; }

        public string Id { get; set; } = null!;
        
        public string Class { get; set; } = null!;

        public string Module { get; set; } = null!;

        public object[] Args { get; set; } = null!;

        public Dictionary<string, object> KWArgs { get; set; } = null!;

        public string Tag { get; set; } = null!;

        public ModelStatus Status { get; set; }
    }
}
