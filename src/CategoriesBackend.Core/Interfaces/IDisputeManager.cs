using CategoriesBackend.Core.Models;

namespace CategoriesBackend.Core.Interfaces;

public interface IDisputeManager
{
    /// <summary>Scans locked round answers, flags any that don't start with the round letter, persists them, and returns the ordered queue.</summary>
    Task<List<Dispute>> DetectDisputesAsync(string gameId, CancellationToken ct = default);

    Task OpenDisputeVotingAsync(string gameId, string category, string answer, CancellationToken ct = default);
    /// <summary>Cast a valid/invalid vote. Returns (voteCount, totalVoters, resolved, isValid).</summary>
    Task<(int VoteCount, int TotalVoters, bool Resolved, bool IsValid)> CastDisputeVoteAsync(string gameId, string votingPlayerId, string disputeId, bool isValid, CancellationToken ct = default);
    Task CloseDisputeVotingAsync(string gameId, string disputeId, CancellationToken ct = default);
    /// <summary>Auto-resolve all pending disputes in a category (called on category advance). Tie = valid.</summary>
    Task ResolveAllPendingForCategoryAsync(string gameId, string category, CancellationToken ct = default);
}
