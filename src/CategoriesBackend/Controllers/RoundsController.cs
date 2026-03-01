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

        await roundManager.EndRoundAsync(gameId, ct);

        var round = await roundManager.GetCurrentRoundAsync(gameId, ct);
        await hub.Clients.Group(gameId).SendAsync(GameHubEvents.RoundEnded, new
        {
            roundNumber = round.RoundNumber,
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
                roundNumber = round.RoundNumber,
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
}

public record SubmitAnswersRequest(string PlayerId, Dictionary<string, string> Answers);
public record EndRoundRequest(string PlayerId);
