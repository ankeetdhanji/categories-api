using CategoriesBackend.Core.Interfaces;
using CategoriesBackend.Core.Models;

namespace CategoriesBackend.Core.Managers;

public class HostModerationManager(IGameRepository gameRepository, IScoringEngine scoringEngine) : IHostModerationManager
{
    public Task<RoundScoreResult> RejectAnswerAsync(
        string gameId, string hostPlayerId,
        string category, string normalizedAnswer,
        CancellationToken ct = default)
    {
        var key = $"{category}:{normalizedAnswer}";
        return RecalculateAndSave(gameId, hostPlayerId, round => round.RejectedAnswerIds.Add(key), ct);
    }

    public Task<RoundScoreResult> UnrejectAnswerAsync(
        string gameId, string hostPlayerId,
        string category, string normalizedAnswer,
        CancellationToken ct = default)
    {
        var key = $"{category}:{normalizedAnswer}";
        return RecalculateAndSave(gameId, hostPlayerId, round => round.RejectedAnswerIds.Remove(key), ct);
    }

    public async Task<(MergeGroup Group, RoundScoreResult Scores)> MergeAnswersAsync(
        string gameId, string hostPlayerId,
        string category, List<string> normalizedAnswers, string canonicalAnswer,
        CancellationToken ct = default)
    {
        // Generate the ID outside the transaction lambda so it is stable across retries.
        var groupId = Guid.NewGuid().ToString();
        MergeGroup? capturedGroup = null;

        var scores = await RecalculateAndSave(gameId, hostPlayerId, round =>
        {
            var group = new MergeGroup
            {
                Id = groupId,
                Category = category,
                CanonicalAnswer = canonicalAnswer,
                MergedNormalizedAnswers = [.. normalizedAnswers],
            };
            round.MergeGroups.Add(group);
            capturedGroup = group;
        }, ct);

        return (capturedGroup!, scores);
    }

    public Task<RoundScoreResult> UnmergeAnswersAsync(
        string gameId, string hostPlayerId,
        string mergeGroupId,
        CancellationToken ct = default)
    {
        return RecalculateAndSave(gameId, hostPlayerId,
            round => round.MergeGroups.RemoveAll(g => g.Id == mergeGroupId), ct);
    }

    /// <summary>
    /// Applies <paramref name="mutation"/> to the current round inside a Firestore transaction, then
    /// recomputes and persists scores atomically. Retried automatically on write conflicts.
    /// </summary>
    private async Task<RoundScoreResult> RecalculateAndSave(
        string gameId, string hostPlayerId,
        Action<Round> mutation,
        CancellationToken ct)
    {
        return await gameRepository.RunInTransactionAsync<RoundScoreResult>(gameId, game =>
        {
            VerifyHost(game, hostPlayerId);

            var round = game.Rounds[game.CurrentRoundIndex];
            mutation(round);

            var invalidDisputeIds = round.Disputes
                .Where(d => d.Status == DisputeStatus.Invalid)
                .Select(d => d.Id)
                .ToHashSet();

            var allExclusions = new HashSet<string>(invalidDisputeIds);
            allExclusions.UnionWith(round.RejectedAnswerIds);

            var moderation = new ModerationContext(allExclusions, round.MergeGroups);
            var newScores = scoringEngine.ComputeRoundScores(round, game.Settings, moderation);

            foreach (var player in game.Players)
            {
                if (player.IsSpectating) continue;
                var oldScore = round.RoundScores.GetValueOrDefault(player.Id, 0);
                var newScore = newScores.GetValueOrDefault(player.Id, 0);
                player.TotalScore += newScore - oldScore;
            }

            round.RoundScores = newScores;

            var leaderboard = game.Players
                .OrderByDescending(p => p.TotalScore)
                .Select(p => new LeaderboardEntry(
                    p.Id, p.DisplayName, p.TotalScore,
                    newScores.GetValueOrDefault(p.Id, 0)))
                .ToList();

            var result = new RoundScoreResult(round.RoundNumber, newScores, leaderboard);
            return (result, game);
        }, ct);
    }

    private static void VerifyHost(Game game, string playerId)
    {
        if (game.HostPlayerId != playerId)
            throw new UnauthorizedAccessException("Only the host can moderate answers.");
    }
}
