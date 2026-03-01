using CategoriesBackend.Core.Enums;
using CategoriesBackend.Core.Interfaces;
using CategoriesBackend.Core.Models;

namespace CategoriesBackend.Core.Managers;

public class RoundManager(IGameRepository gameRepository, IScoringEngine scoringEngine) : IRoundManager
{
    public async Task StartRoundAsync(string gameId, CancellationToken ct = default)
    {
        // Intentionally a no-op here; BeginRoundAsync on IGameManager handles round initialisation.
        await Task.CompletedTask;
    }

    public async Task SubmitAnswersAsync(string gameId, string playerId, Dictionary<string, string> answers, CancellationToken ct = default)
    {
        var game = await GetGameAsync(gameId, ct);

        if (game.Status != GameStatus.InRound)
            throw new InvalidOperationException("No round is currently in progress.");

        var round = game.Rounds[game.CurrentRoundIndex];

        if (round.Status == RoundStatus.Locked)
            throw new InvalidOperationException("Round is locked; no further submissions accepted.");

        var normalized = answers.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Trim().ToLowerInvariant());

        round.Answers[playerId] = new PlayerAnswers
        {
            PlayerId = playerId,
            Answers = new Dictionary<string, string>(answers),
            NormalizedAnswers = normalized,
            IsSubmitted = true,
        };

        await gameRepository.SaveAsync(game, ct);
    }

    public async Task EndRoundAsync(string gameId, CancellationToken ct = default)
    {
        var game = await GetGameAsync(gameId, ct);
        var round = game.Rounds[game.CurrentRoundIndex];

        if (round.Status == RoundStatus.Locked) return; // idempotent

        round.Status = RoundStatus.Locked;
        await gameRepository.SaveAsync(game, ct);
    }

    public async Task<RoundScoreResult> ScoreRoundAsync(string gameId, CancellationToken ct = default)
    {
        var game = await GetGameAsync(gameId, ct);
        var round = game.Rounds[game.CurrentRoundIndex];

        var roundScores = scoringEngine.ComputeRoundScores(round, game.Settings);

        // Persist the per-round breakdown on the round itself
        round.RoundScores = roundScores;

        // Add to each player's running total
        foreach (var player in game.Players)
        {
            if (roundScores.TryGetValue(player.Id, out var pts))
                player.TotalScore += pts;
        }

        game.Status = GameStatus.RoundResults;
        await gameRepository.SaveAsync(game, ct);

        var leaderboard = game.Players
            .OrderByDescending(p => p.TotalScore)
            .Select(p => new LeaderboardEntry(
                p.Id,
                p.DisplayName,
                p.TotalScore,
                roundScores.TryGetValue(p.Id, out var r) ? r : 0))
            .ToList();

        return new RoundScoreResult(round.RoundNumber, roundScores, leaderboard);
    }

    public async Task<Round> GetCurrentRoundAsync(string gameId, CancellationToken ct = default)
    {
        var game = await GetGameAsync(gameId, ct);

        if (game.CurrentRoundIndex < 0 || game.CurrentRoundIndex >= game.Rounds.Count)
            throw new InvalidOperationException("No active round.");

        return game.Rounds[game.CurrentRoundIndex];
    }

    private async Task<Game> GetGameAsync(string gameId, CancellationToken ct)
        => await gameRepository.GetByIdAsync(gameId, ct)
           ?? throw new InvalidOperationException($"Game '{gameId}' not found.");
}
