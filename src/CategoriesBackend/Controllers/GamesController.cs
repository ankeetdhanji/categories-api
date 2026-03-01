using CategoriesBackend.Core.Interfaces;
using CategoriesBackend.Core.Models;
using CategoriesBackend.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace CategoriesBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GamesController(IGameManager gameManager, IRoundManager roundManager, IHubContext<GameHub> hub) : ControllerBase
{
    /// <summary>Creates a new game and returns the join code.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(CreateGameResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateGame([FromBody] CreateGameRequest request, CancellationToken ct)
    {
        // TODO: resolve playerId from auth token once auth is wired
        var game = await gameManager.CreateGameAsync(request.HostPlayerId, request.DisplayName, ct);
        var response = new CreateGameResponse(game.Id, game.JoinCode, GameSettingsDto.From(game.Settings));
        return CreatedAtAction(nameof(GetGame), new { gameId = game.Id }, response);
    }

    /// <summary>Joins an existing game by join code.</summary>
    [HttpPost("{joinCode}/join")]
    [ProducesResponseType(typeof(JoinGameResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> JoinGame(string joinCode, [FromBody] JoinGameRequest request, CancellationToken ct)
    {
        var playerAlreadyInGame = false;
        var game = await gameManager.GetGameByJoinCodeAsync(joinCode, ct);
        if (game != null) playerAlreadyInGame = game.Players.Any(p => p.Id == request.PlayerId);

        game = await gameManager.JoinGameAsync(joinCode, request.PlayerId, request.DisplayName, ct);

        if (!playerAlreadyInGame)
        {
            var newPlayer = game.Players.First(p => p.Id == request.PlayerId);
            await hub.Clients.Group(game.Id).SendAsync(
                GameHubEvents.PlayerJoined,
                PlayerDto.From(newPlayer),
                ct);
        }

        return Ok(new JoinGameResponse(game.Id, game.Players.Select(PlayerDto.From).ToList(), GameSettingsDto.From(game.Settings)));
    }

    /// <summary>Starts the game (host only). Broadcasts a synced countdown then transitions to InRound.</summary>
    [HttpPost("{gameId}/start")]
    [ProducesResponseType(typeof(StartGameResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> StartGame(string gameId, [FromBody] StartGameRequest request, CancellationToken ct)
    {
        var startAt = await gameManager.StartGameAsync(gameId, request.PlayerId, ct);

        await hub.Clients.Group(gameId).SendAsync(
            GameHubEvents.GameCountdown,
            new { startAt },
            ct);

        // Fire-and-forget: begin round after countdown, then lock it when the timer expires
        var delay = startAt - DateTimeOffset.UtcNow;
        _ = Task.Run(async () =>
        {
            await Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.Zero);
            var round = await gameManager.BeginRoundAsync(gameId);
            await hub.Clients.Group(gameId).SendAsync(GameHubEvents.RoundStarted, new
            {
                roundNumber = round.RoundNumber,
                letter = round.Letter.ToString(),
                categories = round.Categories,
                startedAt = round.StartedAt,
                endsAt = round.EndedAt,
            });

            if (round.EndedAt.HasValue)
            {
                var roundDelay = round.EndedAt.Value - DateTimeOffset.UtcNow;
                await Task.Delay(roundDelay > TimeSpan.Zero ? roundDelay : TimeSpan.Zero);
                await roundManager.EndRoundAsync(gameId);
                await hub.Clients.Group(gameId).SendAsync(GameHubEvents.RoundEnded, new
                {
                    roundNumber = round.RoundNumber,
                });

                var result = await roundManager.ScoreRoundAsync(gameId);
                await hub.Clients.Group(gameId).SendAsync(GameHubEvents.LeaderboardUpdated, new
                {
                    roundNumber = result.RoundNumber,
                    roundScores = result.RoundScores,
                    leaderboard = result.Leaderboard,
                });
            }
        });

        return Ok(new StartGameResponse(startAt));
    }

    /// <summary>Gets the current state of a game.</summary>
    [HttpGet("{gameId}")]
    [ProducesResponseType(typeof(Game), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetGame(string gameId, CancellationToken ct)
    {
        var game = await gameManager.GetGameAsync(gameId, ct);
        return Ok(game);
    }
}

// --- Request models ---
public record CreateGameRequest(string HostPlayerId, string DisplayName);
public record JoinGameRequest(string PlayerId, string DisplayName);
public record StartGameRequest(string PlayerId);
public record StartGameResponse(DateTimeOffset StartAt);

// --- Response models ---
public record CreateGameResponse(string GameId, string JoinCode, GameSettingsDto Settings);
public record JoinGameResponse(string GameId, List<PlayerDto> Players, GameSettingsDto Settings);

public record GameSettingsDto(
    bool IsTimedMode,
    int RoundDurationSeconds,
    int MaxRounds,
    int MaxPlayers,
    int UniqueAnswerPoints,
    int SharedAnswerPoints,
    int BestAnswerBonusPoints,
    int DisputeVotingWindowSeconds,
    List<string> Categories)
{
    public static GameSettingsDto From(GameSettings s) => new(
        s.IsTimedMode, s.RoundDurationSeconds, s.MaxRounds, s.MaxPlayers,
        s.UniqueAnswerPoints, s.SharedAnswerPoints, s.BestAnswerBonusPoints,
        s.DisputeVotingWindowSeconds, s.Categories);
}

public record PlayerDto(string Id, string DisplayName, bool IsHost, bool IsGuest, int TotalScore)
{
    public static PlayerDto From(Player p) => new(p.Id, p.DisplayName, false, p.IsGuest, p.TotalScore);
}
