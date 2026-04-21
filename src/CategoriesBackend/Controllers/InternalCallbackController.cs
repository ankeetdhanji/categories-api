using CategoriesBackend.Core.Interfaces;
using CategoriesBackend.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace CategoriesBackend.Controllers;

/// <summary>
/// Internal callback endpoints invoked by Cloud Tasks (production) or NoOpSchedulingService (local dev).
/// All handlers are idempotent — safe to execute twice.
/// Session ID is included in task payloads and checked here so stale tasks from prior game sessions
/// (e.g. after ReopenLobby) are silently ignored.
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
    public async Task<IActionResult> BeginRound(string gameId, [FromQuery] string? sessionId, CancellationToken ct)
    {
        if (await IsStaleSession(gameId, sessionId, ct)) return Ok();

        var round = await gameManager.BeginRoundAsync(gameId, ct);
        var game = await gameManager.GetGameAsync(gameId, ct);

        await hub.Clients.Group(gameId).SendAsync(GameHubEvents.RoundStarted, new
        {
            roundNumber = round.RoundNumber,
            letter = round.Letter.ToString(),
            categories = round.Categories,
            startedAt = round.StartedAt,
            endsAt = round.EndedAt,
        }, ct);

        await hub.Clients.Group(gameId).SendAsync(GameHubEvents.GameStateSync, ct);

        if (round.EndedAt.HasValue)
        {
            var delay = round.EndedAt.Value - DateTimeOffset.UtcNow;
            await schedulingService.ScheduleRoundEndAsync(gameId, game.SessionId, delay > TimeSpan.Zero ? delay : TimeSpan.Zero, ct);
        }

        return Ok();
    }

    /// <summary>Called when the timed round expires. Ends the round, scores it, and queues the next.</summary>
    [HttpPost("end-round")]
    public async Task<IActionResult> EndRound(string gameId, [FromQuery] string? sessionId, CancellationToken ct)
    {
        if (await IsStaleSession(gameId, sessionId, ct)) return Ok();

        var game = await gameManager.GetGameAsync(gameId, ct);
        var currentRound = game.Rounds[game.CurrentRoundIndex];

        await RoundEndCascade.ExecuteAsync(gameId, currentRound.RoundNumber, roundManager, disputeManager, schedulingService, hub, gameManager, ct);

        return Ok();
    }

    /// <summary>Called after leaderboard buffer. Begins the next round.</summary>
    [HttpPost("begin-next-round")]
    public async Task<IActionResult> BeginNextRound(string gameId, [FromQuery] string? sessionId, CancellationToken ct)
    {
        if (await IsStaleSession(gameId, sessionId, ct)) return Ok();

        var round = await gameManager.BeginNextRoundAsync(gameId, ct);
        if (round == null) return Ok(); // all rounds played — host finalizes manually

        var game = await gameManager.GetGameAsync(gameId, ct);

        await hub.Clients.Group(gameId).SendAsync(GameHubEvents.RoundStarted, new
        {
            roundNumber = round.RoundNumber,
            letter = round.Letter.ToString(),
            categories = round.Categories,
            startedAt = round.StartedAt,
            endsAt = round.EndedAt,
        }, ct);

        await hub.Clients.Group(gameId).SendAsync(GameHubEvents.GameStateSync, ct);

        if (round.EndedAt.HasValue)
        {
            var delay = round.EndedAt.Value - DateTimeOffset.UtcNow;
            await schedulingService.ScheduleRoundEndAsync(gameId, game.SessionId, delay > TimeSpan.Zero ? delay : TimeSpan.Zero, ct);
        }

        return Ok();
    }

    /// <summary>Called when a dispute's voting window expires. Resolves any outstanding votes using tie-rule defaults.</summary>
    [HttpPost("disputes/{disputeId}/close")]
    public async Task<IActionResult> CloseDispute(string gameId, string disputeId, [FromQuery] string? sessionId, CancellationToken ct)
    {
        if (await IsStaleSession(gameId, sessionId, ct)) return Ok();

        await disputeManager.CloseDisputeVotingAsync(gameId, disputeId, ct);
        return Ok();
    }

    /// <summary>
    /// Called after the host grace window expires. Transfers host to the next connected player.
    /// If no eligible player exists, marks the game as abandoned and broadcasts GameAbandoned.
    /// </summary>
    [HttpPost("transfer-host")]
    public async Task<IActionResult> TransferHost(string gameId, [FromQuery] string? sessionId, CancellationToken ct)
    {
        if (await IsStaleSession(gameId, sessionId, ct)) return Ok();

        var game = await gameManager.GetGameAsync(gameId, ct);
        if (!game.IsAwaitingHost) return Ok(); // host reconnected within grace window — no-op

        var currentHostId = game.HostPlayerId;
        var newHostId = await gameManager.TransferHostAsync(gameId, currentHostId, ct);
        await gameManager.ResolveHostAwaitAsync(gameId, ct);

        if (newHostId != null)
        {
            await hub.Clients.Group(gameId).SendAsync(GameHubEvents.HostChanged, new { hostPlayerId = newHostId }, ct);
        }
        else
        {
            // No connected players found — abandon the game
            await gameManager.MarkGameAbandonedAsync(gameId, ct);
            await hub.Clients.Group(gameId).SendAsync(GameHubEvents.GameAbandoned, new { gameId }, ct);
        }

        return Ok();
    }

    /// <summary>
    /// Returns true if the task's sessionId doesn't match the current game session,
    /// meaning this is a stale task from a prior session (e.g. before ReopenLobby).
    /// </summary>
    private async Task<bool> IsStaleSession(string gameId, string? sessionId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(sessionId)) return false;
        try
        {
            var game = await gameManager.GetGameAsync(gameId, ct);
            if (game.SessionId != sessionId)
            {
                Console.WriteLine($"[InternalCallback] Stale task ignored for game {gameId}: task sessionId={sessionId}, current={game.SessionId}");
                return true;
            }
            return false;
        }
        catch
        {
            return false; // if game not found, let the handler deal with it
        }
    }
}
