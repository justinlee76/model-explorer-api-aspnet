using ModelStoreApi.Domain;
using ModelStoreApi.JsonConverters;
using System.Text.Json.Serialization;

namespace ModelStoreApi.Dtos
{
    public record JobDto(
        string Id,
        DateTime DateTime,
        string Status,
        string Task,
        string Args,
        string KWArgs,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        JobErrorDto? Error
    )
    {
        public JobDto(Job job)
            : this(
                  job.Id.ToString(),
                  job.DateTime,
                  job.Status.ToString(),
                  $"{job.Module}.{job.Class}",
                  BsonConverter.Serialize(job.Args, false),
                  BsonConverter.Serialize(job.KWArgs, false),
                  job.Error is null ? null : new JobErrorDto(job.Error)
              )
        {
        }
    }
}
