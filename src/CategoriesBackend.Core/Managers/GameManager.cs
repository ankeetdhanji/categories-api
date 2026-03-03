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

        if (game.Status == GameStatus.Finished)
            throw new InvalidOperationException("Game has already finished.");

        if (game.Players.Count >= game.Settings.MaxPlayers)
            throw new InvalidOperationException("Game is full.");

        if (game.Players.Any(p => p.Id == playerId))
            return game; // already in the game

        var isSpectating = game.Status != GameStatus.Lobby;
        game.Players.Add(new Player { Id = playerId, DisplayName = displayName, IsConnected = true, IsSpectating = isSpectating });
        await gameRepository.SaveAsync(game, ct);
        return game;
    }

    public async Task<StartGameResult> StartGameAsync(string gameId, string requestingPlayerId, CancellationToken ct = default)
    {
        var game = await GetGameAsync(gameId, ct);

        if (game.HostPlayerId != requestingPlayerId)
            throw new UnauthorizedAccessException("Only the host can start the game.");

        if (game.Status != GameStatus.Lobby)
            throw new InvalidOperationException("Game is not in lobby state.");

        // Pre-generate rounds so the letter is known before the countdown fires
        game.Rounds = GenerateRounds(game.Settings);

        var startAt = DateTimeOffset.UtcNow.AddSeconds(5);
        game.Status = GameStatus.Starting;
        await gameRepository.SaveAsync(game, ct);

        var firstRound = game.Rounds[0];
        return new StartGameResult(startAt, firstRound.Letter, firstRound.RoundNumber);
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

        // Late joiners who were spectating are now active participants
        foreach (var player in game.Players)
            player.IsSpectating = false;

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
            : GameSettings.DefaultCategories;

        return letters.Select((letter, i) => new Round
        {
            RoundNumber = i + 1,
            Letter = letter,
            Categories = [.. categories],
        }).ToList();
    }

    public async Task<Game> GetGameAsync(string gameId, CancellationToken ct = default)
    {
        return await gameRepository.GetByIdAsync(gameId, ct)
            ?? throw new InvalidOperationException($"Game '{gameId}' not found.");
    }

    public Task<Game?> GetGameByJoinCodeAsync(string joinCode, CancellationToken ct = default)
        => gameRepository.GetByJoinCodeAsync(joinCode, ct);

    public async Task UpdateGameSettingsAsync(string gameId, string requestingPlayerId, GameSettings settings, CancellationToken ct = default)
    {
        var game = await gameRepository.GetByIdAsync(gameId, ct)
            ?? throw new KeyNotFoundException($"Game '{gameId}' not found.");

        if (game.HostPlayerId != requestingPlayerId)
            throw new UnauthorizedAccessException("Only the host can change settings.");

        if (game.Status != GameStatus.Lobby)
            throw new InvalidOperationException("Settings can only be changed in the lobby.");

        game.Settings = settings;
        await gameRepository.SaveAsync(game, ct);
    }

    public async Task<BestAnswerBonusResult> ApplyBestAnswerBonusAsync(string gameId, string requestingPlayerId, CancellationToken ct = default)
    {
        var game = await gameRepository.GetByIdAsync(gameId, ct)
            ?? throw new KeyNotFoundException($"Game '{gameId}' not found.");

        if (game.HostPlayerId != requestingPlayerId)
            throw new UnauthorizedAccessException("Only the host can finalize the game.");

        // Tally how many likes each player's answers received, across all rounds and categories.
        // For each like, find every player who gave that normalized answer in that category.
        var votesByPlayer = game.Players.ToDictionary(p => p.Id, _ => 0);

        foreach (var round in game.Rounds)
        {
            foreach (var (category, categoryLikes) in round.CategoryLikes)
            {
                foreach (var (_, likedNorm) in categoryLikes)
                {
                    foreach (var (authorId, playerAnswers) in round.Answers)
                    {
                        if (playerAnswers.NormalizedAnswers.TryGetValue(category, out var norm)
                            && norm == likedNorm
                            && votesByPlayer.ContainsKey(authorId))
                        {
                            votesByPlayer[authorId]++;
                        }
                    }
                }
            }
        }

        // Persist best-answer vote counts on each player
        foreach (var player in game.Players)
            player.BestAnswerVotes = votesByPlayer.GetValueOrDefault(player.Id);

        // Determine winner(s): highest votes (only award if at least 1 vote was cast in total)
        var maxVotes = votesByPlayer.Values.DefaultIfEmpty(0).Max();
        var winnerIds = maxVotes > 0
            ? votesByPlayer.Where(kv => kv.Value == maxVotes).Select(kv => kv.Key).ToList()
            : [];

        // Integer split: bonus per winner (floor division; remainder is dropped)
        var bonusPerWinner = winnerIds.Count > 0 ? game.Settings.BestAnswerBonusPoints / winnerIds.Count : 0;

        foreach (var player in game.Players)
        {
            if (winnerIds.Contains(player.Id))
                player.TotalScore += bonusPerWinner;
        }

        game.Status = GameStatus.Finished;
        await gameRepository.SaveAsync(game, ct);

        var finalLeaderboard = game.Players
            .OrderByDescending(p => p.TotalScore)
            .ThenByDescending(p => p.BestAnswerVotes)
            .Select(p => new FinalLeaderboardEntry(p.Id, p.DisplayName, p.TotalScore, p.BestAnswerVotes))
            .ToList();

        return new BestAnswerBonusResult(votesByPlayer, winnerIds, bonusPerWinner, finalLeaderboard);
    }

    private static string GenerateJoinCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return new string(Enumerable.Range(0, 6).Select(_ => chars[Random.Shared.Next(chars.Length)]).ToArray());
    }
}
