using CategoriesBackend.Core.Models;

namespace CategoriesBackend.Core.Interfaces;

public interface IScoringEngine
{
    /// <summary>
    /// Computes scores for a completed round. Unique answers score more than shared ones.
    /// </summary>
    Dictionary<string, int> ComputeRoundScores(Round round, GameSettings settings);
}
