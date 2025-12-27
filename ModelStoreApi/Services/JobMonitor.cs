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
            LogInformation("Processing job change: {change}", change);
            
            switch (change.OperationType)
            {
                case ChangeStreamOperationType.Insert:
                    await SendAddJobAsync(new JobDto(change.FullDocument));
                    break;
                case ChangeStreamOperationType.Update:
                    if (change.FullDocument != null)
                    {
                        await SendUpdateJobAsync(new JobDto(change.FullDocument));
                    }
                    break;
                case ChangeStreamOperationType.Delete:
                    if (change.DocumentKey.TryGetValue("_id", out var bsonValue) && bsonValue.IsObjectId)
                    {
                        var jobId = bsonValue.AsObjectId.ToString();
                        await SendRemoveJobAsync(jobId);
                    }
                    break;
            }
        }

        private async System.Threading.Tasks.Task SendAddJobAsync(JobDto job)
        {
            LogInformation("AddJob: {job}", job);
            await _hubContext.Clients.All.SendAsync("AddJob", job);
        }

        private async System.Threading.Tasks.Task SendUpdateJobAsync(JobDto job)
        {
            LogInformation("UpdateJob: {job}", job);
            await _hubContext.Clients.All.SendAsync("UpdateJob", job);
        }

        private async System.Threading.Tasks.Task SendRemoveJobAsync(string jobId)
        {
            LogInformation("RemoveJob: {jobId}", jobId);
            await _hubContext.Clients.All.SendAsync("RemoveJob", jobId);
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
