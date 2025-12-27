using MongoDB.Driver;

namespace ModelStoreApi.Dtos
{
    public record UpdateResponse(long ModifiedCount)
    {
        public UpdateResponse(UpdateResult result)
            : this(result.IsModifiedCountAvailable ? result.ModifiedCount : 0)
        {
        }
    }
}
