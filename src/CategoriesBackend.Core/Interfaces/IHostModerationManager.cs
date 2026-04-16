using CategoriesBackend.Core.Models;

namespace CategoriesBackend.Core.Interfaces;

public interface IHostModerationManager
{
    Task<RoundScoreResult> RejectAnswerAsync(string gameId, string hostPlayerId, string category, string normalizedAnswer, CancellationToken ct = default);
    Task<RoundScoreResult> UnrejectAnswerAsync(string gameId, string hostPlayerId, string category, string normalizedAnswer, CancellationToken ct = default);
    Task<(MergeGroup Group, RoundScoreResult Scores)> MergeAnswersAsync(string gameId, string hostPlayerId, string category, List<string> normalizedAnswers, string canonicalAnswer, CancellationToken ct = default);
    Task<RoundScoreResult> UnmergeAnswersAsync(string gameId, string hostPlayerId, string mergeGroupId, CancellationToken ct = default);
}
