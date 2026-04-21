using CategoriesBackend.Core.Interfaces;
using Google.Cloud.Tasks.V2;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using CloudTask = Google.Cloud.Tasks.V2.Task;
using SystemTask = System.Threading.Tasks.Task;

namespace CategoriesBackend.Infrastructure.Services;

public class CloudTasksSchedulingService(CloudTasksClient tasksClient, string projectId, string locationId, string queueId, string callbackBaseUrl) : ISchedulingService
{
    private QueueName QueueName => new(projectId, locationId, queueId);

    public async SystemTask ScheduleBeginRoundAsync(string gameId, string sessionId, TimeSpan delay, CancellationToken ct = default)
    {
        var taskName = $"begin-round-{gameId}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        await CreateHttpTaskAsync(taskName, $"/internal/games/{gameId}/begin-round?sessionId={sessionId}", delay, ct);
    }

    public async SystemTask ScheduleRoundEndAsync(string gameId, string sessionId, TimeSpan delay, CancellationToken ct = default)
    {
        var taskName = $"end-round-{gameId}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        await CreateHttpTaskAsync(taskName, $"/internal/games/{gameId}/end-round?sessionId={sessionId}", delay, ct);
    }

    public async SystemTask ScheduleNextRoundAsync(string gameId, string sessionId, TimeSpan delay, CancellationToken ct = default)
    {
        var taskName = $"next-round-{gameId}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        await CreateHttpTaskAsync(taskName, $"/internal/games/{gameId}/begin-next-round?sessionId={sessionId}", delay, ct);
    }

    public async SystemTask ScheduleDisputeCloseAsync(string gameId, string sessionId, string disputeId, TimeSpan delay, CancellationToken ct = default)
    {
        var taskName = $"close-dispute-{gameId}-{disputeId}";
        await CreateHttpTaskAsync(taskName, $"/internal/games/{gameId}/disputes/{disputeId}/close?sessionId={sessionId}", delay, ct);
    }

    public async SystemTask ScheduleHostTransferAsync(string gameId, string sessionId, int delaySeconds, CancellationToken ct = default)
    {
        // Deterministic task name — Cloud Tasks will reject duplicates so rapid ReopenLobby retries
        // produce only one scheduled transfer (Gap #15).
        var taskName = $"host-transfer-{gameId}";
        try
        {
            await CreateHttpTaskAsync(taskName, $"/internal/games/{gameId}/transfer-host?sessionId={sessionId}", TimeSpan.FromSeconds(delaySeconds), ct);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.AlreadyExists)
        {
            // Task already queued — intentional deduplication; no action needed.
        }
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
