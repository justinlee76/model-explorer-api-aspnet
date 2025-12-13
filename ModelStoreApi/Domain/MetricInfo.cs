using MongoDB.Bson;

namespace ModelStoreApi.Domain
{
    public record MetricInfo(ObjectId ModelId, string MetricName);
}
