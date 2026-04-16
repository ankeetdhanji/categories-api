using CategoriesBackend.Core.Interfaces;
using CategoriesBackend.Core.Models;

namespace CategoriesBackend.Core.Managers;

public class HostModerationManager(IGameRepository gameRepository, IScoringEngine scoringEngine) : IHostModerationManager
{
    public async Task<RoundScoreResult> RejectAnswerAsync(
        string gameId, string hostPlayerId,
        string category, string normalizedAnswer,
        CancellationToken ct = default)
    {
        var game = await GetGameAsync(gameId, ct);
        VerifyHost(game, hostPlayerId);

        var round = game.Rounds[game.CurrentRoundIndex];
        var key = $"{category}:{normalizedAnswer}";
        round.RejectedAnswerIds.Add(key);

        return await RecalculateAndSave(game, round, ct);
    }

    public async Task<RoundScoreResult> UnrejectAnswerAsync(
        string gameId, string hostPlayerId,
        string category, string normalizedAnswer,
        CancellationToken ct = default)
    {
        var game = await GetGameAsync(gameId, ct);
        VerifyHost(game, hostPlayerId);

        var round = game.Rounds[game.CurrentRoundIndex];
        var key = $"{category}:{normalizedAnswer}";
        round.RejectedAnswerIds.Remove(key);

        return await RecalculateAndSave(game, round, ct);
    }

    public async Task<(MergeGroup Group, RoundScoreResult Scores)> MergeAnswersAsync(
        string gameId, string hostPlayerId,
        string category, List<string> normalizedAnswers, string canonicalAnswer,
        CancellationToken ct = default)
    {
        var game = await GetGameAsync(gameId, ct);
        VerifyHost(game, hostPlayerId);

        var round = game.Rounds[game.CurrentRoundIndex];

        var group = new MergeGroup
        {
            Id = Guid.NewGuid().ToString(),
            Category = category,
            CanonicalAnswer = canonicalAnswer,
            MergedNormalizedAnswers = [.. normalizedAnswers],
        };
        round.MergeGroups.Add(group);

        var scores = await RecalculateAndSave(game, round, ct);
        return (group, scores);
    }

    public async Task<RoundScoreResult> UnmergeAnswersAsync(
        string gameId, string hostPlayerId,
        string mergeGroupId,
        CancellationToken ct = default)
    {
        var game = await GetGameAsync(gameId, ct);
        VerifyHost(game, hostPlayerId);

        var round = game.Rounds[game.CurrentRoundIndex];
        round.MergeGroups.RemoveAll(g => g.Id == mergeGroupId);

        return await RecalculateAndSave(game, round, ct);
    }

    private async Task<RoundScoreResult> RecalculateAndSave(Game game, Round round, CancellationToken ct)
    {
        var invalidDisputeIds = round.Disputes
            .Where(d => d.Status == DisputeStatus.Invalid)
            .Select(d => d.Id)
            .ToHashSet();

        // Combine invalid dispute IDs with host rejections
        var allExclusions = new HashSet<string>(invalidDisputeIds);
        allExclusions.UnionWith(round.RejectedAnswerIds);

        var moderation = new ModerationContext(allExclusions, round.MergeGroups);
        var newScores = scoringEngine.ComputeRoundScores(round, game.Settings, moderation);

        // Delta-apply score changes
        foreach (var player in game.Players)
        {
            if (player.IsSpectating) continue;
            var oldScore = round.RoundScores.GetValueOrDefault(player.Id, 0);
            var newScore = newScores.GetValueOrDefault(player.Id, 0);
            player.TotalScore += newScore - oldScore;
        }

        round.RoundScores = newScores;
        await gameRepository.SaveAsync(game, ct);

        var leaderboard = game.Players
            .OrderByDescending(p => p.TotalScore)
            .Select(p => new LeaderboardEntry(
                p.Id, p.DisplayName, p.TotalScore,
                newScores.GetValueOrDefault(p.Id, 0)))
            .ToList();

        return new RoundScoreResult(round.RoundNumber, newScores, leaderboard);
    }

    private static void VerifyHost(Game game, string playerId)
    {
        if (game.HostPlayerId != playerId)
            throw new UnauthorizedAccessException("Only the host can moderate answers.");
    }

    private async Task<Game> GetGameAsync(string gameId, CancellationToken ct)
        => await gameRepository.GetByIdAsync(gameId, ct)
           ?? throw new InvalidOperationException($"Game '{gameId}' not found.");
}
