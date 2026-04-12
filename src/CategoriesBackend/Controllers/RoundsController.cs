using CategoriesBackend.Core.Interfaces;
using CategoriesBackend.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace CategoriesBackend.Controllers;

[ApiController]
[Route("api/games/{gameId}/[controller]")]
public class RoundsController(
    IGameManager gameManager,
    IRoundManager roundManager,
    IDisputeManager disputeManager,
    ISchedulingService schedulingService,
    IHubContext<GameHub> hub) : ControllerBase
{
    /// <summary>Host-only: begins the countdown for the next round and broadcasts GameCountdown to all clients.</summary>
    [HttpPost("next")]
    public async Task<IActionResult> StartNextRound(string gameId, [FromBody] StartNextRoundRequest request, CancellationToken ct)
    {
        var game = await gameManager.GetGameAsync(gameId, ct);
        if (game.HostPlayerId != request.PlayerId)
            return Forbid();

        var result = await gameManager.PrepareNextRoundAsync(gameId, ct);
        if (result == null)
            return BadRequest("All rounds have been played.");

        await hub.Clients.Group(gameId).SendAsync(GameHubEvents.GameCountdown, new
        {
            startAt = result.StartAt,
            letter = result.Letter.ToString(),
            roundNumber = result.RoundNumber,
        }, ct);

        var delay = result.StartAt - DateTimeOffset.UtcNow;
        await schedulingService.ScheduleNextRoundAsync(gameId, delay > TimeSpan.Zero ? delay : TimeSpan.Zero, ct);

        return Ok();
    }

    [HttpGet("current")]
    public async Task<IActionResult> GetCurrentRound(string gameId, CancellationToken ct)
    {
        var round = await roundManager.GetCurrentRoundAsync(gameId, ct);
        return Ok(round);
    }

    [HttpPost("current/answers")]
    public async Task<IActionResult> SubmitAnswers(string gameId, [FromBody] SubmitAnswersRequest request, CancellationToken ct)
    {
        var accepted = await roundManager.SubmitAnswersAsync(gameId, request.PlayerId, request.Answers, ct);
        if (accepted)
        {
            await hub.Clients.Group(gameId).SendAsync(
                GameHubEvents.PlayerSubmitted,
                new { playerId = request.PlayerId },
                ct);
        }

        return Ok();
    }

    /// <summary>Host-only: force-ends the current round, scores it, and broadcasts results.</summary>
    [HttpPost("current/end")]
    public async Task<IActionResult> EndRound(string gameId, [FromBody] EndRoundRequest request, CancellationToken ct)
    {
        var game = await gameManager.GetGameAsync(gameId, ct);
        if (game.HostPlayerId != request.PlayerId)
            return Forbid();

        var currentRound = game.Rounds[game.CurrentRoundIndex];

        await RoundEndCascade.ExecuteAsync(gameId, currentRound.RoundNumber, roundManager, disputeManager, schedulingService, hub, gameManager, ct);

        return Ok();
    }

    /// <summary>Player marks themselves as done for the current round. Auto-ends when all connected players are done.</summary>
    [HttpPost("current/done")]
    public async Task<IActionResult> MarkDone(string gameId, [FromBody] MarkDoneRequest request, CancellationToken ct)
    {
        var game = await gameManager.GetGameAsync(gameId, ct);
        var currentRound = game.Rounds[game.CurrentRoundIndex];

        var allDone = await roundManager.MarkPlayerDoneAsync(gameId, request.PlayerId, ct);

        await hub.Clients.Group(gameId).SendAsync(GameHubEvents.PlayerDone, new
        {
            playerId = request.PlayerId,
        }, ct);

        if (!allDone) return Ok(new { allDone = false });

        await RoundEndCascade.ExecuteAsync(gameId, currentRound.RoundNumber, roundManager, disputeManager, schedulingService, hub, gameManager, ct);

        return Ok(new { allDone = true });
    }

    [HttpGet("{roundNumber}/results")]
    public async Task<IActionResult> GetRoundResults(string gameId, int roundNumber, CancellationToken ct)
    {
        var results = await roundManager.GetRoundResultsAsync(gameId, roundNumber, ct);
        return Ok(results);
    }

    [HttpPost("{roundNumber}/disputes/{disputeId}/vote")]
    public async Task<IActionResult> VoteOnDispute(
        string gameId, int roundNumber, string disputeId,
        [FromBody] DisputeVoteRequest request, CancellationToken ct)
    {
        var (voteCount, totalVoters, resolved, isValid) =
            await disputeManager.CastDisputeVoteAsync(gameId, request.PlayerId, disputeId, request.IsValid, ct);

        await hub.Clients.Group(gameId).SendAsync(GameHubEvents.DisputeVoteUpdated, new
        {
            disputeId,
            voteCount,
            totalVoters,
        }, ct);

        if (resolved)
        {
            await hub.Clients.Group(gameId).SendAsync(GameHubEvents.DisputeResolved, new
            {
                disputeId,
                isValid,
            }, ct);
        }

        return Ok(new { voteCount, totalVoters, resolved, isValid });
    }

    [HttpPost("{roundNumber}/likes")]
    public async Task<IActionResult> LikeAnswer(
        string gameId, int roundNumber,
        [FromBody] LikeAnswerRequest request, CancellationToken ct)
    {
        await roundManager.LikeAnswerAsync(gameId, roundNumber, request.PlayerId, request.Category, request.NormalizedAnswer, ct);
        return Ok();
    }

    /// <summary>Host-only: advance to next category in review phase. Idempotent: only advances if
    /// request.CurrentCategoryIndex matches the persisted round index.</summary>
    [HttpPost("current/review/advance")]
    public async Task<IActionResult> AdvanceCategory(string gameId, [FromBody] AdvanceCategoryRequest request, CancellationToken ct)
    {
        var game = await gameManager.GetGameAsync(gameId, ct);
        if (game.HostPlayerId != request.PlayerId)
            return Forbid();

        var round = game.Rounds[game.CurrentRoundIndex];

        // Idempotency: if already advanced past this index, return current state
        if (round.CurrentCategoryIndex != request.CurrentCategoryIndex)
        {
            var alreadyLast = round.CurrentCategoryIndex >= round.Categories.Count;
            return Ok(new { categoryIndex = round.CurrentCategoryIndex, isLastCategory = alreadyLast });
        }

        // Resolve all pending disputes in the current category before advancing
        if (request.CurrentCategoryIndex >= 0 && request.CurrentCategoryIndex < round.Categories.Count)
        {
            var currentCategory = round.Categories[request.CurrentCategoryIndex];
            await disputeManager.ResolveAllPendingForCategoryAsync(gameId, currentCategory, ct);
        }

        var nextIndex = request.CurrentCategoryIndex + 1;
        var isLastCategory = nextIndex >= round.Categories.Count;

        // Persist the new index atomically to prevent double-advance races
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

        return Ok(new { categoryIndex = nextIndex, isLastCategory });
    }
}

public record StartNextRoundRequest(string PlayerId);
public record SubmitAnswersRequest(string PlayerId, Dictionary<string, string> Answers);
public record EndRoundRequest(string PlayerId);
public record MarkDoneRequest(string PlayerId);
public record DisputeVoteRequest(string PlayerId, bool IsValid);
public record LikeAnswerRequest(string PlayerId, string Category, string NormalizedAnswer);
public record AdvanceCategoryRequest(string PlayerId, int CurrentCategoryIndex);
