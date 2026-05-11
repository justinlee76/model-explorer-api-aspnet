using ModelStoreApi.Domain;
using System.Text.Json;
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
                  job.Id,
                  job.DateTime,
                  job.Status.ToString(),
                  job.TaskId,
                  JsonSerializer.Serialize(job.Args),
                  JsonSerializer.Serialize(job.KWArgs),
                  job.ModelId,
                  job.Error is null ? null : new JobErrorDto(job.Error)
              )
        {
        }
    }

}
