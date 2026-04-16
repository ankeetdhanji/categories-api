namespace CategoriesBackend.Core.Interfaces;

public interface ISchedulingService
{
    /// <summary>Schedule the initial round begin (after countdown) for game start.</summary>
    Task ScheduleBeginRoundAsync(string gameId, TimeSpan delay, CancellationToken ct = default);
    /// <summary>Schedule end of the current timed round.</summary>
    Task ScheduleRoundEndAsync(string gameId, TimeSpan delay, CancellationToken ct = default);
    /// <summary>Schedule the start of the next round (used after leaderboard).</summary>
    Task ScheduleNextRoundAsync(string gameId, TimeSpan delay, CancellationToken ct = default);
    Task ScheduleDisputeCloseAsync(string gameId, string disputeId, TimeSpan delay, CancellationToken ct = default);
    Task CancelScheduledTaskAsync(string taskName, CancellationToken ct = default);
}
