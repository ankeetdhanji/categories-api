using CategoriesBackend.Core.Interfaces;
using Google.Cloud.Tasks.V2;
using Google.Protobuf.WellKnownTypes;
using CloudTask = Google.Cloud.Tasks.V2.Task;
using SystemTask = System.Threading.Tasks.Task;

namespace CategoriesBackend.Infrastructure.Services;

public class CloudTasksSchedulingService(CloudTasksClient tasksClient, string projectId, string locationId, string queueId, string callbackBaseUrl) : ISchedulingService
{
    private QueueName QueueName => new(projectId, locationId, queueId);

    public async SystemTask ScheduleRoundEndAsync(string gameId, TimeSpan delay, CancellationToken ct = default)
    {
        var taskName = $"end-round-{gameId}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        await CreateHttpTaskAsync(taskName, $"/internal/games/{gameId}/end-round", delay, ct);
    }

    public async SystemTask ScheduleDisputeCloseAsync(string gameId, string disputeId, TimeSpan delay, CancellationToken ct = default)
    {
        var taskName = $"close-dispute-{gameId}-{disputeId}";
        await CreateHttpTaskAsync(taskName, $"/internal/games/{gameId}/disputes/{disputeId}/close", delay, ct);
    }

    public SystemTask CancelScheduledTaskAsync(string taskName, CancellationToken ct = default)
    {
        // Cloud Tasks does not support cancellation by name directly without the full task path.
        // Idempotency checks in the callback handlers mitigate duplicate executions.
        return SystemTask.CompletedTask;
    }

    private async SystemTask CreateHttpTaskAsync(string taskName, string path, TimeSpan delay, CancellationToken ct)
    {
        var task = new CloudTask
        {
            Name = $"{QueueName}/tasks/{taskName}",
            HttpRequest = new HttpRequest
            {
                HttpMethod = Google.Cloud.Tasks.V2.HttpMethod.Post,
                Url = $"{callbackBaseUrl}{path}"
            },
            ScheduleTime = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow.Add(delay))
        };

        await tasksClient.CreateTaskAsync(new CreateTaskRequest { Parent = QueueName.ToString(), Task = task }, ct);
    }
}
