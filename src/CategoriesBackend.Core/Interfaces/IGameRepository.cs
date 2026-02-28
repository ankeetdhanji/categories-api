using CategoriesBackend.Core.Models;

namespace CategoriesBackend.Core.Interfaces;

public interface IGameRepository
{
    Task<Game?> GetByIdAsync(string gameId, CancellationToken ct = default);
    Task<Game?> GetByJoinCodeAsync(string joinCode, CancellationToken ct = default);
    Task SaveAsync(Game game, CancellationToken ct = default);
    Task DeleteAsync(string gameId, CancellationToken ct = default);
}
