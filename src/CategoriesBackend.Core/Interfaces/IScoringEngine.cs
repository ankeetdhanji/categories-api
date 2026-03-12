using CategoriesBackend.Core.Models;

namespace CategoriesBackend.Core.Interfaces;

public interface IScoringEngine
{
    /// <summary>
    /// Computes scores for a completed round. Unique answers score more than shared ones.
    /// </summary>
    Dictionary<string, int> ComputeRoundScores(Round round, GameSettings settings);

    /// <summary>
    /// Computes scores for a completed round, excluding answers whose dispute ID appears in
    /// <paramref name="invalidDisputeIds"/>. Pass <c>null</c> to include all answers (equivalent
    /// to the overload without this parameter).
    /// </summary>
    Dictionary<string, int> ComputeRoundScores(
        Round round,
        GameSettings settings,
        IReadOnlySet<string>? invalidDisputeIds);
}
