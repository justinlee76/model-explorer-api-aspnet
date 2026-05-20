using Microsoft.AspNetCore.Mvc;
using ModelStoreApi.Domain;
using ModelStoreApi.Dtos;
using ModelStoreApi.Services;
using System.Text.Json;

namespace ModelStoreApi.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class ModelStoreController : ControllerBase
    {
        private static readonly JsonSerializerOptions s_indentedJsonSerializerOptions = new()
        {
            WriteIndented = true
        };

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
        public async Task<ActionResult<IEnumerable<ModelDto>>> GetTrainingStats(string tag)
        {
            var trainingStats = await _modelStore.GetModelsForTagAsync(tag);
            var statViews = trainingStats.Select(s => new ModelDto(s)).ToList();
            return statViews;
        }

        [HttpPost]
        public async Task<ActionResult<IEnumerable<MetricHistoryDto>>> GetTrainingData([FromBody] MetricHistoryRequest? request)
        {
            if (!TryValidateMetricHistoryRequest(request, out var errorMessage))
                return BadRequest(errorMessage);

            var seriesKeys = request!.SeriesKeys!;
            var keys = seriesKeys.Select(s => new MetricHistoryKey(s.ModelId, s.MetricName)).ToArray();
            var trainingData = await _modelStore.GetMetricHistoryAsync(keys);
            var dict = trainingData.ToDictionary(d => new SeriesKey(d.Id, d.MetricName));
            var histories = new List<MetricHistory>();
            foreach (var seriesKey in seriesKeys)
            {
                if (dict.TryGetValue(seriesKey, out var history))
                {
                    histories.Add(history);
                }
                else
                {
                    histories.Add(new MetricHistory { Id = seriesKey.ModelId, MetricName = seriesKey.MetricName, Values = [] });
                }
            }
            var seriesList = histories.Select(d => new MetricHistoryDto(d.Id, d.MetricName, d.Values)).ToList();
            return seriesList;
        }

        private static bool TryValidateMetricHistoryRequest(MetricHistoryRequest? request, out string errorMessage)
        {
            if (request is null)
            {
                errorMessage = "Request body is required.";
                return false;
            }

            if (request.SeriesKeys is null)
            {
                errorMessage = "seriesKeys is required.";
                return false;
            }

            for (var i = 0; i < request.SeriesKeys.Length; i++)
            {
                var seriesKey = request.SeriesKeys[i];
                if (seriesKey is null)
                {
                    errorMessage = $"seriesKeys[{i}] is required.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(seriesKey.ModelId))
                {
                    errorMessage = $"seriesKeys[{i}].modelId is required.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(seriesKey.MetricName))
                {
                    errorMessage = $"seriesKeys[{i}].metricName is required.";
                    return false;
                }
            }

            errorMessage = string.Empty;
            return true;
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

            var id = job?.Id;
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

                taskId = lastJob.TaskId;
                args = JsonSerializer.Serialize(lastJob.Args, s_indentedJsonSerializerOptions);
                kwargs = JsonSerializer.Serialize(lastJob.KWArgs, s_indentedJsonSerializerOptions);
            }
            else
            {
                var tasks = await _modelStore.GetTasksAsync();
                taskId = tasks.FirstOrDefault()?.Id;
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
