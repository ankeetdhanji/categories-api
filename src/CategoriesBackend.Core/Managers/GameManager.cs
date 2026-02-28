using CategoriesBackend.Core.Enums;
using CategoriesBackend.Core.Interfaces;
using CategoriesBackend.Core.Models;

namespace CategoriesBackend.Core.Managers;

public class GameManager(IGameRepository gameRepository) : IGameManager
{
    public async Task<Game> CreateGameAsync(string hostPlayerId, string hostDisplayName, CancellationToken ct = default)
    {
        var game = new Game
        {
            Id = Guid.NewGuid().ToString("N"),
            JoinCode = GenerateJoinCode(),
            HostPlayerId = hostPlayerId,
            Status = GameStatus.Lobby,
            Players =
            [
                new Player
                {
                    Id = hostPlayerId,
                    DisplayName = hostDisplayName,
                    IsConnected = true
                }
            ]
        };

        await gameRepository.SaveAsync(game, ct);
        return game;
    }

    public async Task<Game> JoinGameAsync(string joinCode, string playerId, string displayName, CancellationToken ct = default)
    {
        var game = await gameRepository.GetByJoinCodeAsync(joinCode, ct)
            ?? throw new InvalidOperationException($"Game with join code '{joinCode}' not found.");

        if (game.Status != GameStatus.Lobby)
            throw new InvalidOperationException("Game has already started.");

        if (game.Players.Count >= game.Settings.MaxPlayers)
            throw new InvalidOperationException("Game is full.");

        if (game.Players.Any(p => p.Id == playerId))
            return game; // already in lobby

        game.Players.Add(new Player { Id = playerId, DisplayName = displayName, IsConnected = true });
        await gameRepository.SaveAsync(game, ct);
        return game;
    }

    public async Task<DateTimeOffset> StartGameAsync(string gameId, string requestingPlayerId, CancellationToken ct = default)
    {
        var game = await GetGameAsync(gameId, ct);

        if (game.HostPlayerId != requestingPlayerId)
            throw new UnauthorizedAccessException("Only the host can start the game.");

        if (game.Status != GameStatus.Lobby)
            throw new InvalidOperationException("Game is not in lobby state.");

        var startAt = DateTimeOffset.UtcNow.AddSeconds(5);
        game.Status = GameStatus.Starting;
        await gameRepository.SaveAsync(game, ct);
        return startAt;
    }

    public async Task BeginRoundAsync(string gameId, CancellationToken ct = default)
    {
        var game = await GetGameAsync(gameId, ct);
        if (game.Status != GameStatus.Starting) return;
        game.Status = GameStatus.InRound;
        await gameRepository.SaveAsync(game, ct);
    }

    public async Task<Game> GetGameAsync(string gameId, CancellationToken ct = default)
    {
        return await gameRepository.GetByIdAsync(gameId, ct)
            ?? throw new InvalidOperationException($"Game '{gameId}' not found.");
    }

    public Task<Game?> GetGameByJoinCodeAsync(string joinCode, CancellationToken ct = default)
        => gameRepository.GetByJoinCodeAsync(joinCode, ct);

    private static string GenerateJoinCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return new string(Enumerable.Range(0, 6).Select(_ => chars[Random.Shared.Next(chars.Length)]).ToArray());
    }
}
