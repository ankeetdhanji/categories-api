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
        IGameManager gameManager,
        CancellationToken ct = default)
    {
        var actuallyEnded = await roundManager.EndRoundAsync(gameId, ct);
        if (!actuallyEnded) return; // already ended by a concurrent path — don't re-broadcast stale events

        await hub.Clients.Group(gameId).SendAsync(GameHubEvents.RoundEnded, new
        {
            roundNumber,
        }, ct);

        // Give clients a moment to receive RoundEnded and send their final answers
        // (including the auto-submit triggered by the RoundEnded event and any
        // in-flight blur-saves that may have been issued while the timer wound down).
        await Task.Delay(3000, CancellationToken.None);

        // Wait up to 27 more seconds for any remaining players to submit; poll every 500 ms.
        // Falls through and scores regardless after the deadline.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(27);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(500, CancellationToken.None);
            var game = await gameManager.GetGameAsync(gameId, CancellationToken.None);
            var round = game.Rounds[game.CurrentRoundIndex];
            var activePlayers = game.Players
                .Where(p => p.IsConnected && !p.IsSpectating)
                .Select(p => p.Id)
                .ToHashSet();
            if (activePlayers.Count == 0 ||
                activePlayers.All(id => round.Answers.TryGetValue(id, out var pa) && pa.IsSubmitted))
                break;
        }

        var scoreResult = await roundManager.ScoreRoundAsync(gameId, ct);
        await hub.Clients.Group(gameId).SendAsync(GameHubEvents.LeaderboardUpdated, new
        {
            roundNumber = scoreResult.RoundNumber,
            roundScores = scoreResult.RoundScores,
            leaderboard = scoreResult.Leaderboard,
        }, ct);

        await hub.Clients.Group(gameId).SendAsync(GameHubEvents.GameStateSync, ct);

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
