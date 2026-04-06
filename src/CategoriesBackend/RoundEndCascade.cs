using CategoriesBackend.Core.Interfaces;
using CategoriesBackend.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace CategoriesBackend;

/// <summary>
/// Shared helper that executes the full "end round → score → detect disputes" cascade
/// and broadcasts the results to all clients in the game group.
/// Extracted to avoid duplication between RoundsController, InternalCallbackController, and GameHub.
/// </summary>
internal static class RoundEndCascade
{
    internal static async Task ExecuteAsync(
        string gameId,
        int roundNumber,
        IRoundManager roundManager,
        IDisputeManager disputeManager,
        ISchedulingService schedulingService,
        IHubContext<GameHub> hub,
        CancellationToken ct = default)
    {
        await roundManager.EndRoundAsync(gameId, ct);

        await hub.Clients.Group(gameId).SendAsync(GameHubEvents.RoundEnded, new
        {
            roundNumber,
        }, ct);

        await Task.Delay(TimeSpan.FromSeconds(2), ct); // Grace period for clients to auto-submit

        var scoreResult = await roundManager.ScoreRoundAsync(gameId, ct);
        await hub.Clients.Group(gameId).SendAsync(GameHubEvents.LeaderboardUpdated, new
        {
            roundNumber = scoreResult.RoundNumber,
            roundScores = scoreResult.RoundScores,
            leaderboard = scoreResult.Leaderboard,
        }, ct);

        var disputes = await disputeManager.DetectDisputesAsync(gameId, ct);
        if (disputes.Count > 0)
        {
            await hub.Clients.Group(gameId).SendAsync(GameHubEvents.DisputeFlagged, new
            {
                roundNumber,
                disputes = disputes.Select(d => new
                {
                    id = d.Id,
                    category = d.Category,
                    playerId = d.PlayerId,
                    rawAnswer = d.RawAnswer,
                }),
            }, ct);

            // Schedule dispute close timeout for each unique dispute
            foreach (var dispute in disputes.GroupBy(d => d.Id).Select(g => g.First()))
            {
                await schedulingService.ScheduleDisputeCloseAsync(
                    gameId, dispute.Id,
                    TimeSpan.FromSeconds(30),
                    ct);
            }
        }
    }
}
