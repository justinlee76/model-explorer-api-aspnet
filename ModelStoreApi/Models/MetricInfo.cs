using MongoDB.Bson;

namespace ModelStoreApi.Models
{
    public record MetricInfo(ObjectId ModelId, string MetricName);
}
