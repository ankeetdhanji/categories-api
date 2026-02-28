using CategoriesBackend.Core.Models;

namespace CategoriesBackend.Core.Interfaces;

public interface IGameManager
{
    Task<Game> CreateGameAsync(string hostPlayerId, string hostDisplayName, CancellationToken ct = default);
    Task<Game> JoinGameAsync(string joinCode, string playerId, string displayName, CancellationToken ct = default);
    Task<DateTimeOffset> StartGameAsync(string gameId, string requestingPlayerId, CancellationToken ct = default);
    Task<Round> BeginRoundAsync(string gameId, CancellationToken ct = default);
    Task<Game> GetGameAsync(string gameId, CancellationToken ct = default);
    Task<Game?> GetGameByJoinCodeAsync(string joinCode, CancellationToken ct = default);
}
