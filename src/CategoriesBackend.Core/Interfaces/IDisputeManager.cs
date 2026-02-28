using CategoriesBackend.Core.Models;

namespace CategoriesBackend.Core.Interfaces;

public interface IDisputeManager
{
    Task OpenDisputeVotingAsync(string gameId, string category, string answer, CancellationToken ct = default);
    Task CastDisputeVoteAsync(string gameId, string votingPlayerId, string disputeId, bool isValid, CancellationToken ct = default);
    Task CloseDisputeVotingAsync(string gameId, string disputeId, CancellationToken ct = default);
}
