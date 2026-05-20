using ModelStoreApi.Domain;

namespace ModelStoreApi
{
    public interface IModelStore
    {
        Task<List<string>> GetTagsAsync();
        Task<List<string>> GetMetricNamesAsync();
        Task<List<Model>> GetModelsForTagAsync(string tag);
        Task<List<MetricHistory>> GetMetricHistoryAsync(MetricHistoryKey[] metricHistoryKeys);
        Task<long> DeleteModelsAsync(string[] modelIds);
        IAsyncEnumerable<ModelCollectionChange> MonitorModelsAsync(CancellationToken cancellationToken);
        IAsyncEnumerable<JobCollectionChange> MonitorJobsAsync(CancellationToken cancellationToken);
        Task<List<Domain.Task>> GetTasksAsync();
        Task<Job> InsertJobAsync(JobSubmission job);
        Task<Job> GetLastJobAsync();
        Task<IEnumerable<Job>> GetJobsAsync();
        Task<long> UpdateJobStatusAsync(string jobId, JobStatus status);
        Task<long> DeleteJobAsync(string jobId);
        Task<List<string>> GetJobMessagesAsync(string jobId);
    }
}
