using CategoriesBackend.Core.Models;

namespace CategoriesBackend.Core.Interfaces;

public interface IRoundManager
{
    Task StartRoundAsync(string gameId, CancellationToken ct = default);
    Task<bool> SubmitAnswersAsync(string gameId, string playerId, Dictionary<string, string> answers, CancellationToken ct = default);
    /// <summary>Ends the current round. Returns true if the round was actually ended, false if it was already ended (no-op).</summary>
    Task<bool> EndRoundAsync(string gameId, CancellationToken ct = default);
    Task<RoundScoreResult> ScoreRoundAsync(string gameId, CancellationToken ct = default);
    Task<Round> GetCurrentRoundAsync(string gameId, CancellationToken ct = default);
    Task<RoundReviewResult> GetRoundResultsAsync(string gameId, int roundNumber, CancellationToken ct = default);
    Task LikeAnswerAsync(string gameId, int roundNumber, string playerId, string category, string normalizedAnswer, CancellationToken ct = default);
    /// <summary>Marks a player as done for the current round. Returns true when all connected players are done.</summary>
    Task<bool> MarkPlayerDoneAsync(string gameId, string playerId, CancellationToken ct = default);
    /// <summary>Persists the current review category index. Used to enforce idempotent category advances.</summary>
    Task UpdateCurrentCategoryIndexAsync(string gameId, int index, CancellationToken ct = default);
    /// <summary>
    /// Re-scores the current round excluding Invalid-disputed answers, applies the delta to player
    /// totals and round scores, and saves. Returns a corrected leaderboard. No-ops (no save) when
    /// there are no invalid disputes.
    /// </summary>
    Task<RoundScoreResult> ApplyDisputeCorrectionsAsync(string gameId, CancellationToken ct = default);
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
