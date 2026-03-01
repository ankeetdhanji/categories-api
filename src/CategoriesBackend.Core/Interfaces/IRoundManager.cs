using CategoriesBackend.Core.Models;

namespace CategoriesBackend.Core.Interfaces;

public interface IRoundManager
{
    Task StartRoundAsync(string gameId, CancellationToken ct = default);
    Task SubmitAnswersAsync(string gameId, string playerId, Dictionary<string, string> answers, CancellationToken ct = default);
    Task EndRoundAsync(string gameId, CancellationToken ct = default);
    Task<RoundScoreResult> ScoreRoundAsync(string gameId, CancellationToken ct = default);
    Task<Round> GetCurrentRoundAsync(string gameId, CancellationToken ct = default);
    Task<RoundReviewResult> GetRoundResultsAsync(string gameId, int roundNumber, CancellationToken ct = default);
    Task LikeAnswerAsync(string gameId, int roundNumber, string playerId, string category, string normalizedAnswer, CancellationToken ct = default);
}

public record RoundScoreResult(
    int RoundNumber,
    Dictionary<string, int> RoundScores,
    List<LeaderboardEntry> Leaderboard);

public record LeaderboardEntry(
    string PlayerId,
    string DisplayName,
    int TotalScore,
    int RoundScore);

public record RoundReviewResult(
    int RoundNumber,
    char Letter,
    List<CategoryReview> Categories);

public record CategoryReview(
    string Name,
    List<AnswerEntry> Entries);

public record AnswerEntry(
    string RawAnswer,
    string NormalizedAnswer,
    List<PlayerRef> Players,
    bool IsShared,
    bool IsUnique,
    bool IsDisputed,
    string? DisputeId);

public record PlayerRef(string Id, string DisplayName);
