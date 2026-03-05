using CategoriesBackend.Core.Interfaces;
using CategoriesBackend.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace CategoriesBackend.Controllers;

/// <summary>
/// Internal callback endpoints invoked by Cloud Tasks (production) or NoOpSchedulingService (local dev).
/// All handlers are idempotent — safe to execute twice.
/// </summary>
[ApiController]
[Route("internal/games/{gameId}")]
public class InternalCallbackController(
    IGameManager gameManager,
    IRoundManager roundManager,
    IDisputeManager disputeManager,
    ISchedulingService schedulingService,
    IHubContext<GameHub> hub) : ControllerBase
{
    /// <summary>Called after game countdown. Begins the first round.</summary>
    [HttpPost("begin-round")]
    public async Task<IActionResult> BeginRound(string gameId, CancellationToken ct)
    {
        var round = await gameManager.BeginRoundAsync(gameId, ct);

        await hub.Clients.Group(gameId).SendAsync(GameHubEvents.RoundStarted, new
        {
            roundNumber = round.RoundNumber,
            letter = round.Letter.ToString(),
            categories = round.Categories,
            startedAt = round.StartedAt,
            endsAt = round.EndedAt,
        }, ct);

        if (round.EndedAt.HasValue)
        {
            var delay = round.EndedAt.Value - DateTimeOffset.UtcNow;
            await schedulingService.ScheduleRoundEndAsync(gameId, delay > TimeSpan.Zero ? delay : TimeSpan.Zero, ct);
        }

        return Ok();
    }

    /// <summary>Called when the timed round expires. Ends the round, scores it, and queues the next.</summary>
    [HttpPost("end-round")]
    public async Task<IActionResult> EndRound(string gameId, CancellationToken ct)
    {
        var game = await gameManager.GetGameAsync(gameId, ct);
        var currentRound = game.Rounds[game.CurrentRoundIndex];

        await roundManager.EndRoundAsync(gameId, ct);
        await hub.Clients.Group(gameId).SendAsync(GameHubEvents.RoundEnded, new { roundNumber = currentRound.RoundNumber }, ct);

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
                roundNumber = currentRound.RoundNumber,
                disputes = disputes.Select(d => new { id = d.Id, category = d.Category, playerId = d.PlayerId, rawAnswer = d.RawAnswer }),
            }, ct);
        }

        // Schedule next round if rounds remain (3-second buffer for clients to read leaderboard)
        var freshGame = await gameManager.GetGameAsync(gameId, ct);
        if (freshGame.CurrentRoundIndex + 1 < freshGame.Rounds.Count)
            await schedulingService.ScheduleNextRoundAsync(gameId, TimeSpan.FromSeconds(3), ct);

        return Ok();
    }

    /// <summary>Called after leaderboard buffer. Begins the next round.</summary>
    [HttpPost("begin-next-round")]
    public async Task<IActionResult> BeginNextRound(string gameId, CancellationToken ct)
    {
        var round = await gameManager.BeginNextRoundAsync(gameId, ct);
        if (round == null) return Ok(); // all rounds played — host finalizes manually

        await hub.Clients.Group(gameId).SendAsync(GameHubEvents.RoundStarted, new
        {
            roundNumber = round.RoundNumber,
            letter = round.Letter.ToString(),
            categories = round.Categories,
            startedAt = round.StartedAt,
            endsAt = round.EndedAt,
        }, ct);

        if (round.EndedAt.HasValue)
        {
            var delay = round.EndedAt.Value - DateTimeOffset.UtcNow;
            await schedulingService.ScheduleRoundEndAsync(gameId, delay > TimeSpan.Zero ? delay : TimeSpan.Zero, ct);
        }

        return Ok();
    }
}
