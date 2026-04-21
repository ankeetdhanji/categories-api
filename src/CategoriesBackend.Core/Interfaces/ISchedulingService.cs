namespace CategoriesBackend.Core.Interfaces;

public interface ISchedulingService
{
    /// <summary>Schedule the initial round begin (after countdown) for game start.</summary>
    Task ScheduleBeginRoundAsync(string gameId, string sessionId, TimeSpan delay, CancellationToken ct = default);
    /// <summary>Schedule end of the current timed round.</summary>
    Task ScheduleRoundEndAsync(string gameId, string sessionId, TimeSpan delay, CancellationToken ct = default);
    /// <summary>Schedule the start of the next round (used after leaderboard).</summary>
    Task ScheduleNextRoundAsync(string gameId, string sessionId, TimeSpan delay, CancellationToken ct = default);
    Task ScheduleDisputeCloseAsync(string gameId, string sessionId, string disputeId, TimeSpan delay, CancellationToken ct = default);
    /// <summary>
    /// Schedule a host transfer after <paramref name="delaySeconds"/>. Uses a deterministic task name so rapid
    /// ReopenLobby retries produce only one scheduled task (Cloud Tasks rejects duplicates).
    /// </summary>
    Task ScheduleHostTransferAsync(string gameId, string sessionId, int delaySeconds, CancellationToken ct = default);
    Task CancelScheduledTaskAsync(string taskName, CancellationToken ct = default);
}
