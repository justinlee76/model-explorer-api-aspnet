using ModelStoreApi.Domain;
using ModelStoreApi.JsonConverters;
using System.Text.Json.Serialization;

namespace ModelStoreApi.Dtos
{
    public record JobDto(
        string Id,
        DateTime DateTime,
        string Status,
        string TaskId,
        string Args,
        string KWArgs,
        string? ModelId,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        JobErrorDto? Error
    )
    {
        public JobDto(Job job)
            : this(
                  job.Id.ToString(),
                  job.DateTime,
                  job.Status.ToString(),
                  job.TaskId.ToString(),
                  BsonConverter.Serialize(job.Args, false),
                  BsonConverter.Serialize(job.KWArgs, false),
                  job.ModelId?.ToString(),
                  job.Error is null ? null : new JobErrorDto(job.Error)
              )
        {
        }
    }
}
