using ModelStoreApi.Services;

namespace ModelStoreApi.Dtos
{
    public record TrainingDataRequest(SeriesKey[] SeriesKeys);
}
