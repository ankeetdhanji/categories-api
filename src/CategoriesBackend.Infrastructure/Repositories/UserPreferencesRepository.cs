using CategoriesBackend.Core.Interfaces;
using CategoriesBackend.Core.Models;
using CategoriesBackend.Infrastructure.Persistence;
using Google.Cloud.Firestore;

namespace CategoriesBackend.Infrastructure.Repositories;

public class UserPreferencesRepository(FirestoreDb db) : IUserPreferencesRepository
{
    private CollectionReference Prefs => db.Collection("userPreferences");

    public async Task<UserPreferences?> GetAsync(string playerId, CancellationToken ct = default)
    {
        var snap = await Prefs.Document(playerId).GetSnapshotAsync(ct);
        return snap.Exists ? snap.ConvertTo<UserPreferencesDocument>().ToUserPreferences(playerId) : null;
    }

    public async Task SaveAsync(UserPreferences prefs, CancellationToken ct = default)
    {
        await Prefs.Document(prefs.PlayerId).SetAsync(UserPreferencesDocument.From(prefs), cancellationToken: ct);
    }
}
