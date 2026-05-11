namespace ModelStoreApi.MongoDB
{
    public class MongoModelStoreSettings
    {
        public string Uri { get; set; } = null!;
        public string Database { get; set; } = null!;
        public string ModelCollection {  get; set; } = null!;
        public string TaskCollection { get; set; } = null!;
        public string JobCollection { get; set; } = null!;
    }
}
