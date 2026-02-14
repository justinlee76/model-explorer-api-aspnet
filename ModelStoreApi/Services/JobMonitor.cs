using Microsoft.AspNetCore.SignalR;
using ModelStoreApi.Domain;
using ModelStoreApi.Dtos;
using ModelStoreApi.Hubs;
using MongoDB.Driver;

namespace ModelStoreApi.Services
{
    public class JobMonitor(ModelStoreClient modelStoreClient, IHubContext<JobHub> hubContext, ILogger<JobMonitor> logger) : BackgroundService
    {
        private readonly ModelStoreClient _modelStoreClient = modelStoreClient;
        private readonly IHubContext<JobHub> _hubContext = hubContext;
        private readonly ILogger<JobMonitor> _logger = logger;

        protected override async System.Threading.Tasks.Task ExecuteAsync(CancellationToken stoppingToken)
        {
            LogInformation("Starting monitoring of job collection");
            await _modelStoreClient.MonitorJobsAsync(ProcessJobChangeAsync, stoppingToken);
        }

        private async System.Threading.Tasks.Task ProcessJobChangeAsync(ChangeStreamDocument<Job> change, CancellationToken stoppingToken)
        {
            if (change.DocumentKey.TryGetValue("_id", out var bsonValue) && bsonValue.IsObjectId)
            {
                var jobId = bsonValue.AsObjectId;
                var jobIdStr = jobId.ToString();
                LogInformation("Processing job change for job {JobId}", jobIdStr);

                switch (change.OperationType)
                {
                    case ChangeStreamOperationType.Insert:
                        await SendAddJobAsync(new JobDto(change.FullDocument));
                        break;
                    case ChangeStreamOperationType.Update:
                        if (change.FullDocument != null)
                        {
                            var sendJob = false;
                            foreach (var updatedField in change.UpdateDescription.UpdatedFields)
                            {
                                LogInformation("Field updated for job {JobId}: {FieldName}", jobIdStr, updatedField.Name);
                                var path = updatedField.Name.Split('.');
                                if (path.Length > 0)
                                {
                                    if (path[0] == "logs")
                                    {
                                        if (path.Length == 2)
                                        {
                                            if (int.TryParse(path[1], out var index))
                                            {
                                                var message = await _modelStoreClient.GetJobMessageAtIndexAsync(jobId, index);
                                                await SendAddJobMessage(jobIdStr, index, message);
                                            }
                                        }
                                        else if (path.Length == 1)
                                        {
                                            var messages = await _modelStoreClient.GetJobMessagesAsync(jobId);
                                            for (var i = 0; i < messages.Count; i++)
                                            {
                                                await SendAddJobMessage(jobIdStr, i, messages[i]);
                                            }
                                        }
                                    }
                                    else
                                        sendJob = true;
                                }
                            }
                            if (sendJob)
                                await SendUpdateJobAsync(new JobDto(change.FullDocument));
                        }
                        break;
                    case ChangeStreamOperationType.Delete:
                        await SendRemoveJobAsync(jobIdStr);
                        break;
                }
            }
        }

        private async System.Threading.Tasks.Task SendAddJobAsync(JobDto job)
        {
            LogInformation("AddJob: {Job}", job);
            await _hubContext.Clients.All.SendAsync("AddJob", job);
        }

        private async System.Threading.Tasks.Task SendUpdateJobAsync(JobDto job)
        {
            LogInformation("UpdateJob: {Job}", job);
            await _hubContext.Clients.All.SendAsync("UpdateJob", job);
        }

        private async System.Threading.Tasks.Task SendRemoveJobAsync(string jobId)
        {
            LogInformation("RemoveJob: {JobId}", jobId);
            await _hubContext.Clients.All.SendAsync("RemoveJob", jobId);
        }

        private async System.Threading.Tasks.Task SendAddJobMessage(string jobId, int index, string message)
        {
            LogInformation("AddJobMessage: {JobId}, {Index}, {Message}", jobId, index, message);
            await _hubContext.Clients.Group(jobId).SendAsync("AddJobMessage", jobId, index, message);
        }

        private void LogInformation(string message, params object[] args)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
#pragma warning disable CA2254 // Template should be a static expression
                _logger.LogInformation(message, args);
#pragma warning restore CA2254 // Template should be a static expression
            }
        }
    }
}
