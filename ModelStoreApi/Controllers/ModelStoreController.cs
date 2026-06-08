using Microsoft.AspNetCore.Mvc;
using ModelStoreApi.Domain;
using ModelStoreApi.Dtos;
using ModelStoreApi.Dtos.Api;

namespace ModelStoreApi.Controllers
{
    [ApiController]
    [Route("api")]
    public class ModelStoreController : ControllerBase
    {
        private readonly IModelStore _modelStore;

        public ModelStoreController(IModelStore modelStore)
        {
            _modelStore = modelStore;
        }

        [HttpGet("tags")]
        public async Task<ActionResult<IEnumerable<string>>> GetTags()
        {
            var tags = await _modelStore.GetTagsAsync();
            return tags;
        }

        [HttpGet("metric-names")]
        public async Task<ActionResult<IEnumerable<string>>> GetMetricNames()
        {
            var metricNames = await _modelStore.GetMetricNamesAsync();
            return metricNames;
        }

        [HttpGet("models")]
        public async Task<ActionResult<IEnumerable<ModelData>>> GetModels([FromQuery] string tag)
        {
            var models = await _modelStore.GetModelsForTagAsync(tag);
            return models.Select(ModelData.FromDomain).ToList();
        }

        [HttpPost("metric-history")]
        [ProducesResponseType(typeof(IEnumerable<MetricHistoryData>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(DetailResponse), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<IEnumerable<MetricHistoryData>>> GetMetricHistory([FromBody] List<SeriesKey?>? request)
        {
            if (!TryValidateMetricHistoryRequest(request, out var errorMessage))
                return BadRequest(new DetailResponse(errorMessage));

            var seriesKeys = request!.Select(k => k!).ToArray();
            var keys = seriesKeys.Select(s => new MetricHistoryKey(s.Id, s.MetricName)).ToArray();

            List<MetricHistory> metricHistory;
            try
            {
                metricHistory = await _modelStore.GetMetricHistoryAsync(keys);
            }
            catch (FormatException ex)
            {
                return BadRequest(new DetailResponse(ex.Message));
            }

            var dict = metricHistory.ToDictionary(d => new SeriesKey { Id = d.Id, MetricName = d.MetricName });
            var response = new List<MetricHistoryData>();
            foreach (var seriesKey in seriesKeys)
            {
                if (dict.TryGetValue(seriesKey, out var history))
                {
                    response.Add(MetricHistoryData.FromDomain(history));
                }
                else
                {
                    response.Add(new MetricHistoryData
                    {
                        Id = seriesKey.Id,
                        MetricName = seriesKey.MetricName,
                        Values = []
                    });
                }
            }

            return response;
        }

        private static bool TryValidateMetricHistoryRequest(IReadOnlyList<SeriesKey?>? request, out string errorMessage)
        {
            if (request is null)
            {
                errorMessage = "Request body is required.";
                return false;
            }

            for (var i = 0; i < request.Count; i++)
            {
                var seriesKey = request[i];
                if (seriesKey is null)
                {
                    errorMessage = $"request[{i}] is required.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(seriesKey.Id))
                {
                    errorMessage = $"request[{i}].id is required.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(seriesKey.MetricName))
                {
                    errorMessage = $"request[{i}].metricName is required.";
                    return false;
                }
            }

            errorMessage = string.Empty;
            return true;
        }

        [HttpPost("delete-models")]
        [ProducesResponseType(typeof(DeleteModelsResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(DetailResponse), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<DeleteModelsResponse>> DeleteModels([FromBody] string[]? ids)
        {
            if (ids is null)
                return BadRequest(new DetailResponse("Request body is required."));

            try
            {
                await _modelStore.DeleteModelsAsync(ids);
                return new DeleteModelsResponse();
            }
            catch (FormatException ex)
            {
                return BadRequest(new DetailResponse(ex.Message));
            }
            catch (Exception ex)
            {
                return new DeleteModelsResponse
                {
                    Errors = ids.ToDictionary(id => id, _ => ex.Message)
                };
            }
        }

        [HttpGet("tasks")]
        public async Task<ActionResult<IEnumerable<TaskData>>> GetTasks()
        {
            var tasks = await _modelStore.GetTasksAsync();
            return tasks.Select(TaskData.FromDomain).ToList();
        }

        [HttpPost("add-job")]
        [ProducesResponseType(typeof(AddJobResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(DetailResponse), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<AddJobResponse>> AddJob([FromBody] JobInputs? inputs)
        {
            if (inputs is null)
                return BadRequest(new DetailResponse("Request body is required."));

            var job = await _modelStore.InsertJobAsync(new JobSubmission(inputs.TaskId, inputs.Args, inputs.Kwargs));
            return new AddJobResponse { Id = job.Id };
        }

        [HttpGet("job-defaults")]
        public async Task<ActionResult<JobInputs>> GetJobDefaults()
        {
            var lastJob = await _modelStore.GetLastJobAsync();
            if (lastJob != null)
            {
                return new JobInputs
                {
                    TaskId = lastJob.TaskId,
                    Args = JobInputs.ToJsonElements(lastJob.Args),
                    Kwargs = JobInputs.ToJsonElements(lastJob.KWArgs)
                };
            }

            var tasks = await _modelStore.GetTasksAsync();
            return new JobInputs
            {
                TaskId = tasks.FirstOrDefault()?.Id ?? string.Empty,
                Args = [],
                Kwargs = []
            };
        }

        [HttpGet("jobs")]
        public async Task<ActionResult<IEnumerable<JobData>>> GetJobs()
        {
            var jobs = await _modelStore.GetJobsAsync();
            return jobs.Select(JobData.FromDomain).ToList();
        }

        [HttpPost("stop-job")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(DetailResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(DetailResponse), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> StopJob([FromBody] StopJobRequest? request)
        {
            if (request is null)
                return BadRequest(new DetailResponse("Request body is required."));

            if (string.IsNullOrWhiteSpace(request.Id))
                return BadRequest(new DetailResponse("id is required."));

            long modifiedCount;
            try
            {
                modifiedCount = await _modelStore.UpdateJobStatusAsync(request.Id, JobStatus.Stopping);
            }
            catch (FormatException ex)
            {
                return BadRequest(new DetailResponse(ex.Message));
            }

            if (modifiedCount == 0)
                return NotFound(new DetailResponse("Job not found"));

            return NoContent();
        }

        [HttpDelete("delete-job/{id}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(DetailResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(DetailResponse), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteJob(string id)
        {
            long deletedCount;
            try
            {
                deletedCount = await _modelStore.DeleteJobAsync(id);
            }
            catch (FormatException ex)
            {
                return BadRequest(new DetailResponse(ex.Message));
            }

            if (deletedCount == 0)
                return NotFound(new DetailResponse("Job not found"));

            return NoContent();
        }

        [HttpGet("jobs/{id}/messages")]
        [ProducesResponseType(typeof(IEnumerable<string>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(DetailResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(DetailResponse), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<IEnumerable<string>>> GetJobMessages(string id)
        {
            List<string> messages;
            try
            {
                messages = await _modelStore.GetJobMessagesAsync(id);
            }
            catch (FormatException ex)
            {
                return BadRequest(new DetailResponse(ex.Message));
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new DetailResponse("Job messages not found"));
            }

            if (messages is null)
                return NotFound(new DetailResponse("Job messages not found"));

            return messages;
        }
    }
}
