using CategoriesBackend.Core.Models;

namespace CategoriesBackend.Core.Interfaces;

public interface IGameManager
{
    Task<Game> CreateGameAsync(string hostPlayerId, string hostDisplayName, CancellationToken ct = default);
    Task<Game> JoinGameAsync(string joinCode, string playerId, string displayName, CancellationToken ct = default);
    Task<StartGameResult> StartGameAsync(string gameId, string requestingPlayerId, CancellationToken ct = default);
    Task<Round> BeginRoundAsync(string gameId, CancellationToken ct = default);
    /// <summary>Advances to the next round. Returns null if the game is over (all rounds played).</summary>
    Task<Round?> BeginNextRoundAsync(string gameId, CancellationToken ct = default);
    Task<Game> GetGameAsync(string gameId, CancellationToken ct = default);
    Task<Game?> GetGameByJoinCodeAsync(string joinCode, CancellationToken ct = default);
    Task UpdateGameSettingsAsync(string gameId, string requestingPlayerId, GameSettings settings, CancellationToken ct = default);
    /// <summary>Marks a player as connected or disconnected. No-op if player or game not found.</summary>
    Task SetPlayerConnectedAsync(string gameId, string playerId, bool isConnected, CancellationToken ct = default);
    /// <summary>
    /// Transfers host role from <paramref name="currentHostId"/> to the first other connected player.
    /// Returns the new host's playerId, or null if host has reconnected or no connected player exists.
    /// </summary>
    Task<string?> TransferHostAsync(string gameId, string currentHostId, CancellationToken ct = default);
    /// <summary>
    /// Tallies best-answer likes across all rounds, awards bonus to the top vote-getter(s)
    /// (splitting evenly on a tie), marks the game as Finished, and returns the final standings.
    /// Host only.
    /// </summary>
    Task<BestAnswerBonusResult> ApplyBestAnswerBonusAsync(string gameId, string requestingPlayerId, CancellationToken ct = default);
}

public record StartGameResult(DateTimeOffset StartAt, char Letter, int RoundNumber);

public record BestAnswerBonusResult(
    Dictionary<string, int> VotesByPlayer,
    List<string> WinnerPlayerIds,
    int BonusPerWinner,
    List<FinalLeaderboardEntry> FinalLeaderboard);

public record FinalLeaderboardEntry(string PlayerId, string DisplayName, int TotalScore, int BestAnswerVotes);
