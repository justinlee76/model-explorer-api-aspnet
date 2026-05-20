namespace ModelStoreApi.Domain
{
    public class MetricHistory
    {
        public string Id { get; set; } = null!;
        
        public string MetricName { get; set; } = null!;

        public double[] Values { get; set; } = null!;
    }
}
