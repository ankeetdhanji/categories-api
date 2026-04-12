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

    public async Task<bool> SubmitAnswersAsync(string gameId, string playerId, Dictionary<string, string> answers, CancellationToken ct = default)
    {
        var game = await GetGameAsync(gameId, ct);

        if (game.CurrentRoundIndex < 0 || game.CurrentRoundIndex >= game.Rounds.Count)
            throw new InvalidOperationException("No active round.");

        var normalized = answers.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Trim().ToLowerInvariant());

        var playerAnswers = new PlayerAnswers
        {
            PlayerId = playerId,
            Answers = new Dictionary<string, string>(answers),
            NormalizedAnswers = normalized,
            IsSubmitted = true,
        };

        return await gameRepository.UpdateAnswersAsync(
            gameId, game.CurrentRoundIndex, playerId, playerAnswers, ct);
    }

    public async Task<bool> EndRoundAsync(string gameId, CancellationToken ct = default)
    {
        var game = await GetGameAsync(gameId, ct);
        var round = game.Rounds[game.CurrentRoundIndex];

        if (round.Status == RoundStatus.Locked) return false; // already ended — idempotent

        round.Status = RoundStatus.Locked;
        await gameRepository.SaveAsync(game, ct);
        return true;
    }

    public async Task<RoundScoreResult> ScoreRoundAsync(string gameId, CancellationToken ct = default)
    {
        // Use a Firestore transaction so the read and write are atomic. If a concurrent
        // UpdateAnswersAsync transaction commits between our read and write, Firestore detects
        // the conflict and retries this lambda with a fresh snapshot — ensuring we never
        // overwrite an answer that arrived during the 2-second grace period.
        return await gameRepository.RunInTransactionAsync(gameId, game =>
        {
            var round = game.Rounds[game.CurrentRoundIndex];

            // Idempotency guard: if round was already scored (e.g. timed mode fired and host
            // also force-ended), return the existing result without re-scoring or double-updating totals.
            if (round.RoundScores.Count > 0)
            {
                var existingLeaderboard = game.Players
                    .OrderByDescending(p => p.TotalScore)
                    .Select(p => new LeaderboardEntry(
                        p.Id, p.DisplayName, p.TotalScore,
                        round.RoundScores.TryGetValue(p.Id, out var r) ? r : 0))
                    .ToList();
                return (new RoundScoreResult(round.RoundNumber, round.RoundScores, existingLeaderboard), null);
            }

            var roundScores = scoringEngine.ComputeRoundScores(round, game.Settings);
            round.RoundScores = roundScores;

            foreach (var player in game.Players)
            {
                if (!player.IsSpectating && roundScores.TryGetValue(player.Id, out var pts))
                    player.TotalScore += pts;
            }

            game.Status = GameStatus.RoundResults;

            var leaderboard = game.Players
                .OrderByDescending(p => p.TotalScore)
                .Select(p => new LeaderboardEntry(
                    p.Id, p.DisplayName, p.TotalScore,
                    roundScores.TryGetValue(p.Id, out var r) ? r : 0))
                .ToList();

            return (new RoundScoreResult(round.RoundNumber, roundScores, leaderboard), game);
        }, ct);
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

    public async Task<bool> MarkPlayerDoneAsync(string gameId, string playerId, CancellationToken ct = default)
    {
        var game = await GetGameAsync(gameId, ct);

        if (game.CurrentRoundIndex < 0 || game.CurrentRoundIndex >= game.Rounds.Count)
            throw new InvalidOperationException("No active round.");

        var round = game.Rounds[game.CurrentRoundIndex];

        if (round.Status == RoundStatus.Locked)
            return true; // already ended — treat as all done

        if (!round.DonePlayerIds.Contains(playerId))
        {
            round.DonePlayerIds.Add(playerId);
            await gameRepository.SaveAsync(game, ct);
        }

        var activePlayerIds = game.Players.Where(p => p.IsConnected && !p.IsSpectating).Select(p => p.Id).ToHashSet();
        return activePlayerIds.Count > 0 && activePlayerIds.All(id => round.DonePlayerIds.Contains(id));
    }

    public async Task UpdateCurrentCategoryIndexAsync(string gameId, int index, CancellationToken ct = default)
    {
        var game = await GetGameAsync(gameId, ct);
        game.Rounds[game.CurrentRoundIndex].CurrentCategoryIndex = index;
        await gameRepository.SaveAsync(game, ct);
    }

    public async Task<RoundScoreResult> ApplyDisputeCorrectionsAsync(string gameId, CancellationToken ct = default)
    {
        var game = await GetGameAsync(gameId, ct);
        var round = game.Rounds[game.CurrentRoundIndex];

        var invalidDisputeIds = round.Disputes
            .Where(d => d.Status == DisputeStatus.Invalid)
            .Select(d => d.Id)
            .ToHashSet();

        // Fast path: no corrections needed
        if (invalidDisputeIds.Count == 0)
        {
            var existingLeaderboard = game.Players
                .OrderByDescending(p => p.TotalScore)
                .Select(p => new LeaderboardEntry(
                    p.Id, p.DisplayName, p.TotalScore,
                    round.RoundScores.GetValueOrDefault(p.Id, 0)))
                .ToList();
            return new RoundScoreResult(round.RoundNumber, round.RoundScores, existingLeaderboard);
        }

        var correctedScores = scoringEngine.ComputeRoundScores(round, game.Settings, invalidDisputeIds);

        foreach (var player in game.Players)
        {
            if (player.IsSpectating) continue;
            var oldScore = round.RoundScores.GetValueOrDefault(player.Id, 0);
            var newScore = correctedScores.GetValueOrDefault(player.Id, 0);
            player.TotalScore += newScore - oldScore;
        }

        round.RoundScores = correctedScores;
        await gameRepository.SaveAsync(game, ct);

        var leaderboard = game.Players
            .OrderByDescending(p => p.TotalScore)
            .Select(p => new LeaderboardEntry(
                p.Id, p.DisplayName, p.TotalScore,
                correctedScores.GetValueOrDefault(p.Id, 0)))
            .ToList();

        return new RoundScoreResult(round.RoundNumber, correctedScores, leaderboard);
    }

    private async Task<Game> GetGameAsync(string gameId, CancellationToken ct)
        => await gameRepository.GetByIdAsync(gameId, ct)
           ?? throw new InvalidOperationException($"Game '{gameId}' not found.");
}
