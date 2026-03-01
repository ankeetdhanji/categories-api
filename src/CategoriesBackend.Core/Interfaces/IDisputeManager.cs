using CategoriesBackend.Core.Models;

namespace CategoriesBackend.Core.Interfaces;

public interface IDisputeManager
{
    /// <summary>Scans locked round answers, flags any that don't start with the round letter, persists them, and returns the ordered queue.</summary>
    Task<List<Dispute>> DetectDisputesAsync(string gameId, CancellationToken ct = default);

    Task OpenDisputeVotingAsync(string gameId, string category, string answer, CancellationToken ct = default);
    Task CastDisputeVoteAsync(string gameId, string votingPlayerId, string disputeId, bool isValid, CancellationToken ct = default);
    Task CloseDisputeVotingAsync(string gameId, string disputeId, CancellationToken ct = default);
}
