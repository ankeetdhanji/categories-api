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

        await RoundEndCascade.ExecuteAsync(gameId, currentRound.RoundNumber, roundManager, disputeManager, schedulingService, hub, ct);

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

    /// <summary>Called when a dispute's voting window expires. Resolves any outstanding votes using tie-rule defaults.</summary>
    [HttpPost("disputes/{disputeId}/close")]
    public async Task<IActionResult> CloseDispute(string gameId, string disputeId, CancellationToken ct)
    {
        await disputeManager.CloseDisputeVotingAsync(gameId, disputeId, ct);
        return Ok();
    }

    /// <summary>Called when a category's review window expires. Advances to the next category (handles host-disconnect stall).</summary>
    [HttpPost("advance-category/{currentCategoryIndex:int}")]
    public async Task<IActionResult> AdvanceCategory(string gameId, int currentCategoryIndex, CancellationToken ct)
    {
        var game = await gameManager.GetGameAsync(gameId, ct);
        var round = game.Rounds[game.CurrentRoundIndex];

        // Idempotency: skip if the category has already been advanced
        if (round.CurrentCategoryIndex != currentCategoryIndex)
            return Ok();

        // Resolve any pending disputes in the current category
        if (currentCategoryIndex >= 0 && currentCategoryIndex < round.Categories.Count)
        {
            var currentCategory = round.Categories[currentCategoryIndex];
            await disputeManager.ResolveAllPendingForCategoryAsync(gameId, currentCategory, ct);
        }

        var nextIndex = currentCategoryIndex + 1;
        var isLastCategory = nextIndex >= round.Categories.Count;

        await roundManager.UpdateCurrentCategoryIndexAsync(gameId, nextIndex, ct);

        await hub.Clients.Group(gameId).SendAsync(GameHubEvents.CategoryAdvanced, new
        {
            categoryIndex = nextIndex,
        }, ct);

        if (isLastCategory)
        {
            var corrected = await roundManager.ApplyDisputeCorrectionsAsync(gameId, ct);

            await hub.Clients.Group(gameId).SendAsync(GameHubEvents.LeaderboardUpdated, new
            {
                roundNumber = corrected.RoundNumber,
                roundScores = corrected.RoundScores,
                leaderboard = corrected.Leaderboard,
            }, ct);

            await hub.Clients.Group(gameId).SendAsync(GameHubEvents.ReviewComplete, new
            {
                roundNumber = round.RoundNumber,
            }, ct);
        }
        else
        {
            // Schedule the next auto-advance
            await schedulingService.ScheduleAdvanceCategoryAsync(gameId, nextIndex, TimeSpan.FromSeconds(30), ct);
        }

        return Ok();
    }
}
