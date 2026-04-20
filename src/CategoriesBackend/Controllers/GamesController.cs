using CategoriesBackend.Core.Interfaces;
using CategoriesBackend.Core.Models;
using CategoriesBackend.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace CategoriesBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GamesController(IGameManager gameManager, ISchedulingService schedulingService, IHubContext<GameHub> hub) : ControllerBase
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

        return Ok(new JoinGameResponse(game.Id, (int)game.Status, game.Players.Select(PlayerDto.From).ToList(), GameSettingsDto.From(game.Settings)));
    }

    /// <summary>Starts the game (host only). Broadcasts a synced countdown then transitions to InRound.</summary>
    [HttpPost("{gameId}/start")]
    [ProducesResponseType(typeof(StartGameResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> StartGame(string gameId, [FromBody] StartGameRequest request, CancellationToken ct)
    {
        var result = await gameManager.StartGameAsync(gameId, request.PlayerId, ct);
        var startAt = result.StartAt;

        await hub.Clients.Group(gameId).SendAsync(
            GameHubEvents.GameCountdown,
            new { startAt, letter = result.Letter.ToString(), roundNumber = result.RoundNumber },
            ct);

        // Schedule begin-round after countdown delay
        var delay = startAt - DateTimeOffset.UtcNow;
        await schedulingService.ScheduleBeginRoundAsync(gameId, delay > TimeSpan.Zero ? delay : TimeSpan.Zero, ct);

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

    /// <summary>Updates game settings (host only, lobby phase only).</summary>
    [HttpPut("{gameId}/settings")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateSettings(string gameId, [FromBody] UpdateSettingsRequest request, CancellationToken ct)
    {
        var settings = new GameSettings
        {
            IsTimedMode = request.Settings.IsTimedMode,
            RoundDurationSeconds = request.Settings.RoundDurationSeconds,
            MaxRounds = request.Settings.MaxRounds,
            MaxPlayers = request.Settings.MaxPlayers,
            UniqueAnswerPoints = request.Settings.UniqueAnswerPoints,
            SharedAnswerPoints = request.Settings.SharedAnswerPoints,
            BestAnswerBonusPoints = request.Settings.BestAnswerBonusPoints,
            DisputeVotingWindowSeconds = request.Settings.DisputeVotingWindowSeconds,
            Categories = request.Settings.Categories,
        };

        await gameManager.UpdateGameSettingsAsync(gameId, request.PlayerId, settings, ct);

        await hub.Clients.Group(gameId).SendAsync(
            GameHubEvents.SettingsUpdated,
            new { settings = GameSettingsDto.From(settings) },
            ct);

        return Ok();
    }

    /// <summary>
    /// Finalizes the game (host only): tallies best-answer votes, applies end-game bonus,
    /// marks game as Finished, and broadcasts the final leaderboard.
    /// </summary>
    [HttpPost("{gameId}/finalize")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> FinalizeGame(string gameId, [FromBody] FinalizeGameRequest request, CancellationToken ct)
    {
        var result = await gameManager.ApplyBestAnswerBonusAsync(gameId, request.PlayerId, ct);

        await hub.Clients.Group(gameId).SendAsync(GameHubEvents.LeaderboardUpdated, new
        {
            roundNumber = -1, // -1 signals the final post-bonus leaderboard
            leaderboard = result.FinalLeaderboard,
            winnerPlayerIds = result.WinnerPlayerIds,
            bonusPerWinner = result.BonusPerWinner,
        }, ct);

        return Ok(new
        {
            winnerPlayerIds = result.WinnerPlayerIds,
            bonusPerWinner = result.BonusPerWinner,
            leaderboard = result.FinalLeaderboard,
        });
    }

    /// <summary>Reopens the lobby after the game has finished, resetting scores and allowing a new game.</summary>
    [HttpPost("{gameId}/reopen")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ReopenLobby(string gameId, [FromBody] ReopenLobbyRequest request, CancellationToken ct)
    {
        var result = await gameManager.ReopenLobbyAsync(gameId, request.PlayerId, ct);
        var game = await gameManager.GetGameAsync(gameId, ct);

        var hostAwaitDeadline = result.OriginalHostIsConnected
            ? (DateTimeOffset?)null
            : DateTimeOffset.UtcNow.AddSeconds(30);

        await hub.Clients.Group(gameId).SendAsync(
            GameHubEvents.LobbyReopened,
            new
            {
                hostPlayerId = result.HostPlayerId,
                awaitingHost = !result.OriginalHostIsConnected,
                hostAwaitDeadline,
                players = game.Players.Select(p => new
                {
                    id = p.Id,
                    displayName = p.DisplayName,
                    isHost = p.Id == result.HostPlayerId,
                    isGuest = p.IsGuest,
                    isSpectating = p.IsSpectating,
                    totalScore = p.TotalScore,
                }),
            },
            ct);

        if (!result.OriginalHostIsConnected && result.IsNewReopen)
            _ = SchedulePostReopenHostTransferAsync(gameId, result.HostPlayerId);

        return Ok();
    }

    private async Task SchedulePostReopenHostTransferAsync(string gameId, string currentHostId)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30));

            var game = await gameManager.GetGameAsync(gameId);
            if (!game.IsAwaitingHost) return;

            var newHostId = await gameManager.TransferHostAsync(gameId, currentHostId);
            await gameManager.ResolveHostAwaitAsync(gameId);

            if (newHostId != null)
                await hub.Clients.Group(gameId).SendAsync(GameHubEvents.HostChanged, new { hostPlayerId = newHostId });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[GamesController] SchedulePostReopenHostTransferAsync failed for game {gameId}: {ex.Message}");
        }
    }
}

// --- Request models ---
public record CreateGameRequest(string HostPlayerId, string DisplayName);
public record JoinGameRequest(string PlayerId, string DisplayName);
public record StartGameRequest(string PlayerId);
public record UpdateSettingsRequest(string PlayerId, GameSettingsDto Settings);
public record StartGameResponse(DateTimeOffset StartAt);
public record FinalizeGameRequest(string PlayerId);
public record ReopenLobbyRequest(string PlayerId);

// --- Response models ---
public record CreateGameResponse(string GameId, string JoinCode, GameSettingsDto Settings);
public record JoinGameResponse(string GameId, int Status, List<PlayerDto> Players, GameSettingsDto Settings);

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

public record PlayerDto(string Id, string DisplayName, bool IsHost, bool IsGuest, bool IsSpectating, int TotalScore)
{
    public static PlayerDto From(Player p) => new(p.Id, p.DisplayName, false, p.IsGuest, p.IsSpectating, p.TotalScore);
}
