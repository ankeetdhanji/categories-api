using CategoriesBackend.Core.Interfaces;
using CategoriesBackend.Core.Models;
using CategoriesBackend.Infrastructure.Persistence;
using Google.Cloud.Firestore;

namespace CategoriesBackend.Infrastructure.Repositories;

public class GameRepository(FirestoreDb db) : IGameRepository
{
    private CollectionReference Games => db.Collection("games");

    public async Task<Game?> GetByIdAsync(string gameId, CancellationToken ct = default)
    {
        var snapshot = await Games.Document(gameId).GetSnapshotAsync(ct);
        return snapshot.Exists ? snapshot.ConvertTo<GameDocument>().ToGame() : null;
    }

    public async Task<Game?> GetByJoinCodeAsync(string joinCode, CancellationToken ct = default)
    {
        var query = Games.WhereEqualTo("JoinCode", joinCode).Limit(1);
        var snapshot = await query.GetSnapshotAsync(ct);
        return snapshot.Count > 0 ? snapshot.Documents[0].ConvertTo<GameDocument>().ToGame() : null;
    }

    public async Task SaveAsync(Game game, CancellationToken ct = default)
    {
        var doc = GameDocument.FromGame(game);
        await Games.Document(game.Id).SetAsync(doc, cancellationToken: ct);
    }

    public async Task DeleteAsync(string gameId, CancellationToken ct = default)
    {
        await Games.Document(gameId).DeleteAsync(cancellationToken: ct);
    }
}
