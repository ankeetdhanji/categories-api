using CategoriesBackend.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace CategoriesBackend.Controllers;

[ApiController]
[Route("api/games/{gameId}/[controller]")]
public class RoundsController(IRoundManager roundManager) : ControllerBase
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
        return Ok();
    }

    [HttpPost("current/end")]
    public async Task<IActionResult> EndRound(string gameId, CancellationToken ct)
    {
        // TODO: validate host-only or internal callback
        await roundManager.EndRoundAsync(gameId, ct);
        return Ok();
    }
}

public record SubmitAnswersRequest(string PlayerId, Dictionary<string, string> Answers);
