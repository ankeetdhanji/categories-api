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

    public async Task<bool> UpdateAnswersAsync(
        string gameId,
        int roundIndex,
        string playerId,
        PlayerAnswers playerAnswers,
        CancellationToken ct = default)
    {
        var docRef = Games.Document(gameId);
        bool written = false;

        await db.RunTransactionAsync(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(docRef, ct);
            if (!snapshot.Exists)
                throw new InvalidOperationException($"Game '{gameId}' not found.");

            var game = snapshot.ConvertTo<GameDocument>().ToGame();

            if (roundIndex < 0 || roundIndex >= game.Rounds.Count)
                throw new InvalidOperationException("Round index out of range.");

            var round = game.Rounds[roundIndex];

            if (round.RoundScores.Count > 0)   // already scored — too late
            {
                written = false;
                return;
            }

            round.Answers[playerId] = playerAnswers;
            transaction.Set(docRef, GameDocument.FromGame(game));
            written = true;
        }, cancellationToken: ct);

        return written;
    }

    public async Task DeleteAsync(string gameId, CancellationToken ct = default)
    {
        await Games.Document(gameId).DeleteAsync(cancellationToken: ct);
    }
}
