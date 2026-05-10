using Microsoft.AspNetCore.Mvc;
using ModelStoreApi.Domain;
using ModelStoreApi.Dtos;
using ModelStoreApi.JsonConverters;

namespace ModelStoreApi.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class ModelStoreController : ControllerBase
    {
        private readonly IModelStore _modelStore;

        public ModelStoreController(IModelStore modelStore)
        {
            _modelStore = modelStore;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<string>>> GetTags()
        {
            var tags = await _modelStore.GetTagsAsync();
            return tags;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<string>>> GetMetricNames()
        {
            var metricNames = await _modelStore.GetMetricNamesAsync();
            return metricNames;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<TrainingStatsDto>>> GetTrainingStats(string tag)
        {
            var trainingStats = await _modelStore.GetTrainingStatsForTagAsync(tag);
            var statViews = trainingStats.Select(s => new TrainingStatsDto(s)).ToList();
            return statViews;
        }

        [HttpPost]
        public async Task<ActionResult<IEnumerable<TrainingDataDto>>> GetTrainingData([FromBody] TrainingDataRequest request)
        {
            var metricInfos = request.SeriesKeys.Select(s => new MetricInfo(s.ModelId, s.MetricName)).ToArray();
            var trainingData = await _modelStore.GetTrainingDataAsync(metricInfos);
            var seriesList = trainingData.Select(d => new TrainingDataDto(d.Id.ToString(), d.MetricName, d.MetricHistory)).ToList();
            return seriesList;
        }

        [HttpPost]
        public async Task<ActionResult<DeleteResponse>> DeleteModels([FromBody] DeleteModelsRequest request)
        {
            var result = await _modelStore.DeleteModelsAsync(request.ModelIds);
            return new DeleteResponse(result);
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<TaskDto>>> GetTasks()
        {
            var tasks = await _modelStore.GetTasksAsync();
            return tasks.Select(t => new TaskDto(t)).ToList();
        }

        [HttpPost]
        public async Task<ActionResult<JobResponse>> InsertJob([FromBody] JobRequest request)
        {
            Job? job = null;
            JobErrorDto? error = null;
            try
            {
                job = await _modelStore.InsertJobAsync(new JobSubmission(request.TaskId, request.Args, request.KWArgs));
            }
            catch (Exception ex)
            {
                error = new JobErrorDto(ex.Message, ex.StackTrace);
            }

            var id = job?.Id.ToString() ?? null;
            return new JobResponse(id, error);
        }

        [HttpGet]
        public async Task<ActionResult<JobDefaults>> GetJobDefaults()
        {
            var lastJob = await _modelStore.GetLastJobAsync();
            string? taskId;
            string args;
            string kwargs;
            if (lastJob != null)
            {

                taskId = lastJob.TaskId.ToString();
                args = BsonConverter.Serialize(lastJob.Args, true);
                kwargs = BsonConverter.Serialize(lastJob.KWArgs, true);
            }
            else
            {
                var tasks = await _modelStore.GetTasksAsync();
                taskId = tasks.FirstOrDefault()?.Id.ToString();
                args = "[]";
                kwargs = "{}";
            }

            return new JobDefaults(
                    taskId,
                    args,
                    kwargs
                );
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<JobDto>>> GetJobs()
        {
            var jobs = await _modelStore.GetJobsAsync();
            var jobDtos = jobs.Select(j => new JobDto(j)).ToList();
            return jobDtos;
        }

        [HttpPost]
        public async Task<ActionResult<UpdateResponse>> StopJob([FromBody] StopJobRequest request)
        {
            var result = await _modelStore.UpdateJobStatusAsync(request.JobId, JobStatus.Stopping);
            return new UpdateResponse(result);
        }

        [HttpDelete("{jobId}")]
        public async Task<ActionResult<DeleteResponse>> DeleteJob(string jobId)
        {
            var result = await _modelStore.DeleteJobAsync(jobId);
            return new DeleteResponse(result);
        }

        [HttpGet]
        public async Task<ActionResult<GetJobMessagesResponse>> GetJobMessages(string jobId)
        {
            var messages = await _modelStore.GetJobMessagesAsync(jobId);
            return new GetJobMessagesResponse([.. messages]);
        }
    }
}
