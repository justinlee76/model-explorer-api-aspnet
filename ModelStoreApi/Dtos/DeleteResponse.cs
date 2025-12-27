using MongoDB.Driver;

namespace ModelStoreApi.Dtos
{
    public record DeleteResponse(long DeletedCount)
    {
        public DeleteResponse(DeleteResult result) : this(result.IsAcknowledged ? result.DeletedCount : 0)
        {
        }
    }
}
