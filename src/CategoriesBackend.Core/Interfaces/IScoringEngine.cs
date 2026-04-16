using CategoriesBackend.Core.Models;

namespace CategoriesBackend.Core.Interfaces;

/// <summary>Host-applied moderation state passed to the scoring engine.</summary>
public record ModerationContext(
    IReadOnlySet<string> RejectedAnswerIds,
    IReadOnlyList<MergeGroup> MergeGroups);

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

    /// <summary>
    /// Computes scores applying both dispute corrections and host moderation (rejections + merges).
    /// </summary>
    Dictionary<string, int> ComputeRoundScores(
        Round round,
        GameSettings settings,
        ModerationContext? moderation);
}
