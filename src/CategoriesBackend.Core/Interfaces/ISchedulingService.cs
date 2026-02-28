namespace CategoriesBackend.Core.Interfaces;

public interface ISchedulingService
{
    Task ScheduleRoundEndAsync(string gameId, TimeSpan delay, CancellationToken ct = default);
    Task ScheduleDisputeCloseAsync(string gameId, string disputeId, TimeSpan delay, CancellationToken ct = default);
    Task CancelScheduledTaskAsync(string taskName, CancellationToken ct = default);
}
