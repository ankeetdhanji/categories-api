using CategoriesBackend.Core.Models;

namespace CategoriesBackend.Core.Interfaces;

public interface IRoundManager
{
    Task StartRoundAsync(string gameId, CancellationToken ct = default);
    Task SubmitAnswersAsync(string gameId, string playerId, Dictionary<string, string> answers, CancellationToken ct = default);
    Task EndRoundAsync(string gameId, CancellationToken ct = default);
    Task<RoundScoreResult> ScoreRoundAsync(string gameId, CancellationToken ct = default);
    Task<Round> GetCurrentRoundAsync(string gameId, CancellationToken ct = default);
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
