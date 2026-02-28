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

    public async Task<Round> BeginRoundAsync(string gameId, CancellationToken ct = default)
    {
        var game = await GetGameAsync(gameId, ct);
        if (game.Status != GameStatus.Starting)
            throw new InvalidOperationException("Game is not in Starting state.");

        if (game.Rounds.Count == 0)
            game.Rounds = GenerateRounds(game.Settings);

        game.CurrentRoundIndex = 0;
        game.Status = GameStatus.InRound;

        var round = game.Rounds[0];
        round.Status = RoundStatus.Answering;
        round.StartedAt = DateTimeOffset.UtcNow;
        if (game.Settings.IsTimedMode)
            round.EndedAt = round.StartedAt.Value.AddSeconds(game.Settings.RoundDurationSeconds);

        await gameRepository.SaveAsync(game, ct);
        return round;
    }

    private static List<Round> GenerateRounds(GameSettings settings)
    {
        // Exclude letters that are awkward for this game type
        const string letterPool = "ABCDEFGHIJKLMNOPRSTW";
        var letters = letterPool
            .OrderBy(_ => Random.Shared.Next())
            .Take(settings.MaxRounds)
            .ToList();

        var categories = settings.Categories.Count > 0
            ? settings.Categories
            : DefaultCategories;

        return letters.Select((letter, i) => new Round
        {
            RoundNumber = i + 1,
            Letter = letter,
            Categories = [.. categories],
        }).ToList();
    }

    private static readonly List<string> DefaultCategories =
    [
        "A boy's name", "A girl's name", "A country", "An animal",
        "A city", "A food", "A TV show", "Something you find at school"
    ];

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
