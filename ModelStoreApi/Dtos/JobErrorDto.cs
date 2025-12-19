using ModelStoreApi.Domain;

namespace ModelStoreApi.Dtos
{
    public record JobErrorDto(string Exception, string? StackTrace)
    {
        public JobErrorDto(JobError je)
            : this(je.Exception, je.StackTrace)
        {
        }
    }
}
