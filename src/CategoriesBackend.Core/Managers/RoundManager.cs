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

        if (game.CurrentRoundIndex < 0 || game.CurrentRoundIndex >= game.Rounds.Count)
            throw new InvalidOperationException("No active round.");

        var round = game.Rounds[game.CurrentRoundIndex];

        // Accept late submissions from clients that hadn't submitted before the host
        // force-ended the round (they receive RoundEnded and auto-submit).
        // If the round is already scored, skip silently — the answers are too late.
        if (round.RoundScores.Count > 0)
            return;

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

    public async Task<RoundReviewResult> GetRoundResultsAsync(string gameId, int roundNumber, CancellationToken ct = default)
    {
        var game = await GetGameAsync(gameId, ct);
        var round = game.Rounds.FirstOrDefault(r => r.RoundNumber == roundNumber)
            ?? throw new InvalidOperationException($"Round {roundNumber} not found.");

        // Build a lookup: normalizedAnswer → list of playerIds, per category
        var playerLookup = game.Players.ToDictionary(p => p.Id);

        // disputeId → set of playerIds who authored that disputed answer
        var disputedAnswerIds = round.Disputes
            .GroupBy(d => d.Id)
            .ToDictionary(g => g.Key, g => g.Select(d => d.PlayerId).ToHashSet());

        var categories = round.Categories.Select(category =>
        {
            // Group players by their normalized answer for this category
            var groups = round.Answers.Values
                .Where(pa => pa.NormalizedAnswers.TryGetValue(category, out var norm) && !string.IsNullOrWhiteSpace(norm))
                .GroupBy(pa => pa.NormalizedAnswers[category])
                .ToList();

            var entries = groups.Select(group =>
            {
                var normalizedAnswer = group.Key;
                var disputeId = $"{category}:{normalizedAnswer}";
                var isDisputed = disputedAnswerIds.ContainsKey(disputeId);

                var players = group
                    .Select(pa => playerLookup.TryGetValue(pa.PlayerId, out var p)
                        ? new PlayerRef(p.Id, p.DisplayName)
                        : new PlayerRef(pa.PlayerId, pa.PlayerId))
                    .ToList();

                // Use the raw answer from the first player in the group
                var rawAnswer = group.First().Answers.TryGetValue(category, out var raw) ? raw : normalizedAnswer;

                return new AnswerEntry(
                    RawAnswer: rawAnswer,
                    NormalizedAnswer: normalizedAnswer,
                    Players: players,
                    IsShared: group.Count() > 1,
                    IsUnique: group.Count() == 1,
                    IsDisputed: isDisputed,
                    DisputeId: isDisputed ? disputeId : null);
            }).ToList();

            return new CategoryReview(category, entries);
        }).ToList();

        return new RoundReviewResult(round.RoundNumber, round.Letter, categories);
    }

    public async Task LikeAnswerAsync(string gameId, int roundNumber, string playerId, string category, string normalizedAnswer, CancellationToken ct = default)
    {
        var game = await GetGameAsync(gameId, ct);
        var round = game.Rounds.FirstOrDefault(r => r.RoundNumber == roundNumber)
            ?? throw new InvalidOperationException($"Round {roundNumber} not found.");

        if (!round.CategoryLikes.ContainsKey(category))
            round.CategoryLikes[category] = [];

        round.CategoryLikes[category][playerId] = normalizedAnswer;

        await gameRepository.SaveAsync(game, ct);
    }

    private async Task<Game> GetGameAsync(string gameId, CancellationToken ct)
        => await gameRepository.GetByIdAsync(gameId, ct)
           ?? throw new InvalidOperationException($"Game '{gameId}' not found.");
}
