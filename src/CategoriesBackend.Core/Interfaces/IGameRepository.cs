using CategoriesBackend.Core.Models;

namespace CategoriesBackend.Core.Interfaces;

public interface IGameRepository
{
    Task<Game?> GetByIdAsync(string gameId, CancellationToken ct = default);
    Task<Game?> GetByJoinCodeAsync(string joinCode, CancellationToken ct = default);
    Task SaveAsync(Game game, CancellationToken ct = default);
    Task<bool> UpdateAnswersAsync(string gameId, int roundIndex, string playerId, PlayerAnswers answers, CancellationToken ct = default);
    /// <summary>
    /// Reads the game document inside a Firestore transaction, invokes <paramref name="operation"/>,
    /// and — if the operation returns a non-null updated game — writes it back atomically.
    /// The transaction is retried automatically on write conflicts.
    /// </summary>
    Task<T> RunInTransactionAsync<T>(string gameId, Func<Game, (T result, Game? updatedGame)> operation, CancellationToken ct = default);
    Task DeleteAsync(string gameId, CancellationToken ct = default);
}
