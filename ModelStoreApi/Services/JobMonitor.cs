using Microsoft.AspNetCore.SignalR;
using ModelStoreApi.Domain;
using ModelStoreApi.Dtos;
using ModelStoreApi.Hubs;

namespace ModelStoreApi.Services
{
    public class JobMonitor(IModelStore modelStore, IHubContext<JobHub> hubContext, ILogger<JobMonitor> logger) : BackgroundService
    {
        private readonly IModelStore _modelStore = modelStore;
        private readonly IHubContext<JobHub> _hubContext = hubContext;
        private readonly ILogger<JobMonitor> _logger = logger;

        protected override async System.Threading.Tasks.Task ExecuteAsync(CancellationToken stoppingToken)
        {
            LogInformation("Starting monitoring of job collection");
            await foreach (var change in _modelStore.MonitorJobsAsync(stoppingToken))
            {
                switch (change.Kind)
                {
                    case JobCollectionChangeKind.Added:
                        if (change.Job != null)
                            await SendAddJobAsync(JobData.FromDomain(change.Job));
                        break;
                    case JobCollectionChangeKind.Updated:
                        if (change.Messages != null)
                            await System.Threading.Tasks.Task.WhenAll(change.Messages.Select(SendAddJobMessage));
                        if (change.Job != null)
                            await SendUpdateJobAsync(JobData.FromDomain(change.Job));
                        break;
                    case JobCollectionChangeKind.Removed:
                        await SendRemoveJobAsync(change.JobId);
                        break;
                }
            }
        }

        private async System.Threading.Tasks.Task SendAddJobAsync(JobData job)
        {
            LogInformation("AddJob: {Job}", job);
            await _hubContext.Clients.All.SendAsync("AddJob", job);
        }

        private async System.Threading.Tasks.Task SendUpdateJobAsync(JobData job)
        {
            LogInformation("UpdateJob: {Job}", job);
            await _hubContext.Clients.All.SendAsync("UpdateJob", job);
        }

        private async System.Threading.Tasks.Task SendRemoveJobAsync(string jobId)
        {
            LogInformation("RemoveJob: {JobId}", jobId);
            await _hubContext.Clients.All.SendAsync("RemoveJob", jobId);
        }

        private async System.Threading.Tasks.Task SendAddJobMessage(JobMessageChange message)
        {
            LogInformation("AddJobMessage: {JobId}, {Index}, {Message}", message.JobId, message.Index, message.Message);
            await _hubContext.Clients.Group(message.JobId).SendAsync("AddJobMessage", message.JobId, message.Index, message.Message);
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
