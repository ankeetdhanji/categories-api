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

    public async Task<(int VoteCount, int TotalVoters, bool Resolved, bool IsValid)> CastDisputeVoteAsync(
        string gameId, string votingPlayerId, string disputeId, bool isValid, CancellationToken ct = default)
    {
        var game = await gameRepository.GetByIdAsync(gameId, ct)
            ?? throw new InvalidOperationException($"Game '{gameId}' not found.");

        var round = game.Rounds[game.CurrentRoundIndex];

        // Authors of this dispute cannot vote
        var authorIds = round.Disputes
            .Where(d => d.Id == disputeId)
            .Select(d => d.PlayerId)
            .ToHashSet();

        if (authorIds.Contains(votingPlayerId))
            throw new InvalidOperationException("Answer author cannot vote on their own dispute.");

        // Store vote (overwrites previous vote from same player)
        if (!round.DisputeVotes.ContainsKey(disputeId))
            round.DisputeVotes[disputeId] = [];
        round.DisputeVotes[disputeId][votingPlayerId] = isValid;

        var votes = round.DisputeVotes[disputeId];
        var eligibleVoters = game.Players.Count - authorIds.Count;
        var voteCount = votes.Count;

        // Resolve if all eligible voters have voted
        bool resolved = false;
        bool resolvedIsValid = true;
        if (voteCount >= eligibleVoters && eligibleVoters > 0)
        {
            var validCount = votes.Values.Count(v => v);
            var invalidCount = votes.Values.Count(v => !v);
            resolvedIsValid = validCount >= invalidCount; // tie = valid
            resolved = true;

            var status = resolvedIsValid ? DisputeStatus.Valid : DisputeStatus.Invalid;
            foreach (var d in round.Disputes.Where(d => d.Id == disputeId))
                d.Status = status;
        }

        await gameRepository.SaveAsync(game, ct);
        return (voteCount, eligibleVoters, resolved, resolvedIsValid);
    }

    public async Task ResolveAllPendingForCategoryAsync(string gameId, string category, CancellationToken ct = default)
    {
        var game = await gameRepository.GetByIdAsync(gameId, ct)
            ?? throw new InvalidOperationException($"Game '{gameId}' not found.");

        var round = game.Rounds[game.CurrentRoundIndex];

        foreach (var dispute in round.Disputes.Where(d => d.Category == category && d.Status == DisputeStatus.Pending))
        {
            if (round.DisputeVotes.TryGetValue(dispute.Id, out var votes) && votes.Count > 0)
            {
                var validCount = votes.Values.Count(v => v);
                var invalidCount = votes.Values.Count(v => !v);
                dispute.Status = validCount >= invalidCount ? DisputeStatus.Valid : DisputeStatus.Invalid;
            }
            else
            {
                dispute.Status = DisputeStatus.Valid; // no votes = valid (tie-rule default)
            }
        }

        await gameRepository.SaveAsync(game, ct);
    }

    public Task OpenDisputeVotingAsync(string gameId, string category, string answer, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task CloseDisputeVotingAsync(string gameId, string disputeId, CancellationToken ct = default)
        => Task.CompletedTask;
}
