using CategoriesBackend.Core.Enums;
using CategoriesBackend.Core.Interfaces;
using CategoriesBackend.Core.Models;

namespace CategoriesBackend.Core.Managers;

public class DisputeManager(IGameRepository gameRepository) : IDisputeManager
{
    public async Task<List<Dispute>> DetectDisputesAsync(string gameId, CancellationToken ct = default)
    {
        var game = await gameRepository.GetByIdAsync(gameId, ct)
            ?? throw new InvalidOperationException($"Game '{gameId}' not found.");

        var round = game.Rounds[game.CurrentRoundIndex];
        var expectedLetter = char.ToLowerInvariant(round.Letter);

        var disputes = new List<Dispute>();

        foreach (var (playerId, playerAnswers) in round.Answers)
        {
            foreach (var category in round.Categories)
            {
                if (!playerAnswers.NormalizedAnswers.TryGetValue(category, out var norm)
                    || string.IsNullOrWhiteSpace(norm))
                    continue;

                if (norm[0] != expectedLetter)
                {
                    var raw = playerAnswers.Answers.TryGetValue(category, out var r) ? r : norm;
                    disputes.Add(new Dispute
                    {
                        Id = $"{category}:{norm}",
                        Category = category,
                        PlayerId = playerId,
                        RawAnswer = raw,
                        NormalizedAnswer = norm,
                        Status = DisputeStatus.Pending,
                    });
                }
            }
        }

        // Deterministic order: category asc, then normalized answer asc
        disputes.Sort((a, b) =>
        {
            var cmp = string.Compare(a.Category, b.Category, StringComparison.Ordinal);
            return cmp != 0 ? cmp : string.Compare(a.NormalizedAnswer, b.NormalizedAnswer, StringComparison.Ordinal);
        });

        round.Disputes = disputes;

        if (disputes.Count > 0)
            game.Status = GameStatus.Disputes;

        await gameRepository.SaveAsync(game, ct);

        return disputes;
    }

    // Stubs — implemented in KAN-22
    public Task OpenDisputeVotingAsync(string gameId, string category, string answer, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task CastDisputeVoteAsync(string gameId, string votingPlayerId, string disputeId, bool isValid, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task CloseDisputeVotingAsync(string gameId, string disputeId, CancellationToken ct = default)
        => Task.CompletedTask;
}
