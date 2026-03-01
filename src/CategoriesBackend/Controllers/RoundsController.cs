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
    IHubContext<GameHub> hub) : ControllerBase
{
    [HttpGet("current")]
    public async Task<IActionResult> GetCurrentRound(string gameId, CancellationToken ct)
    {
        var round = await roundManager.GetCurrentRoundAsync(gameId, ct);
        return Ok(round);
    }

    [HttpPost("current/answers")]
    public async Task<IActionResult> SubmitAnswers(string gameId, [FromBody] SubmitAnswersRequest request, CancellationToken ct)
    {
        await roundManager.SubmitAnswersAsync(gameId, request.PlayerId, request.Answers, ct);

        await hub.Clients.Group(gameId).SendAsync(
            GameHubEvents.PlayerSubmitted,
            new { playerId = request.PlayerId },
            ct);

        return Ok();
    }

    /// <summary>Host-only: force-ends the current round, scores it, and broadcasts results.</summary>
    [HttpPost("current/end")]
    public async Task<IActionResult> EndRound(string gameId, [FromBody] EndRoundRequest request, CancellationToken ct)
    {
        var game = await gameManager.GetGameAsync(gameId, ct);
        if (game.HostPlayerId != request.PlayerId)
            return Forbid();

        // Use in-memory round data (already loaded) to avoid an extra Firestore
        // read between EndRoundAsync and the RoundEnded broadcast.
        var currentRound = game.Rounds[game.CurrentRoundIndex];

        await roundManager.EndRoundAsync(gameId, ct);

        await hub.Clients.Group(gameId).SendAsync(GameHubEvents.RoundEnded, new
        {
            roundNumber = currentRound.RoundNumber,
        }, ct);

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
                disputes = disputes.Select(d => new
                {
                    id = d.Id,
                    category = d.Category,
                    playerId = d.PlayerId,
                    rawAnswer = d.RawAnswer,
                }),
            }, ct);
        }

        return Ok();
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

    /// <summary>Host-only: advance to next category in review phase. Auto-resolves pending disputes in the current category.</summary>
    [HttpPost("current/review/advance")]
    public async Task<IActionResult> AdvanceCategory(string gameId, [FromBody] AdvanceCategoryRequest request, CancellationToken ct)
    {
        var game = await gameManager.GetGameAsync(gameId, ct);
        if (game.HostPlayerId != request.PlayerId)
            return Forbid();

        var round = game.Rounds[game.CurrentRoundIndex];

        // Resolve all pending disputes in the current category before advancing
        if (request.CurrentCategoryIndex >= 0 && request.CurrentCategoryIndex < round.Categories.Count)
        {
            var currentCategory = round.Categories[request.CurrentCategoryIndex];
            await disputeManager.ResolveAllPendingForCategoryAsync(gameId, currentCategory, ct);
        }

        var nextIndex = request.CurrentCategoryIndex + 1;
        var isLastCategory = nextIndex >= round.Categories.Count;

        await hub.Clients.Group(gameId).SendAsync(GameHubEvents.CategoryAdvanced, new
        {
            categoryIndex = nextIndex,
        }, ct);

        if (isLastCategory)
        {
            await hub.Clients.Group(gameId).SendAsync(GameHubEvents.ReviewComplete, new
            {
                roundNumber = round.RoundNumber,
            }, ct);
        }

        return Ok(new { categoryIndex = nextIndex, isLastCategory });
    }
}

public record SubmitAnswersRequest(string PlayerId, Dictionary<string, string> Answers);
public record EndRoundRequest(string PlayerId);
public record DisputeVoteRequest(string PlayerId, bool IsValid);
public record LikeAnswerRequest(string PlayerId, string Category, string NormalizedAnswer);
public record AdvanceCategoryRequest(string PlayerId, int CurrentCategoryIndex);
