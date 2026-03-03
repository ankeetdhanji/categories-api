using CategoriesBackend.Core.Models;

namespace CategoriesBackend.Core.Interfaces;

public interface IUserPreferencesRepository
{
    Task<UserPreferences?> GetAsync(string playerId, CancellationToken ct = default);
    Task SaveAsync(UserPreferences prefs, CancellationToken ct = default);
}
