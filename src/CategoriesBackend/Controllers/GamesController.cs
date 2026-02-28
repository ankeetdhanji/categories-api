using CategoriesBackend.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace CategoriesBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GamesController(IGameManager gameManager) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> CreateGame([FromBody] CreateGameRequest request, CancellationToken ct)
    {
        // TODO: resolve playerId from auth token
        var game = await gameManager.CreateGameAsync(request.HostPlayerId, request.DisplayName, ct);
        return Ok(new { game.Id, game.JoinCode });
    }

    [HttpPost("{joinCode}/join")]
    public async Task<IActionResult> JoinGame(string joinCode, [FromBody] JoinGameRequest request, CancellationToken ct)
    {
        var game = await gameManager.JoinGameAsync(joinCode, request.PlayerId, request.DisplayName, ct);
        return Ok(new { game.Id });
    }

    [HttpPost("{gameId}/start")]
    public async Task<IActionResult> StartGame(string gameId, [FromBody] StartGameRequest request, CancellationToken ct)
    {
        await gameManager.StartGameAsync(gameId, request.PlayerId, ct);
        return Ok();
    }

    [HttpGet("{gameId}")]
    public async Task<IActionResult> GetGame(string gameId, CancellationToken ct)
    {
        var game = await gameManager.GetGameAsync(gameId, ct);
        return Ok(game);
    }
}

public record CreateGameRequest(string HostPlayerId, string DisplayName);
public record JoinGameRequest(string PlayerId, string DisplayName);
public record StartGameRequest(string PlayerId);
