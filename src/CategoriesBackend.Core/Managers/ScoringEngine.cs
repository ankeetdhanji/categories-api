using CategoriesBackend.Core.Interfaces;
using CategoriesBackend.Core.Models;

namespace CategoriesBackend.Core.Managers;

public class ScoringEngine : IScoringEngine
{
    public Dictionary<string, int> ComputeRoundScores(Round round, GameSettings settings)
        => ComputeRoundScores(round, settings, (ModerationContext?)null);

    public Dictionary<string, int> ComputeRoundScores(
        Round round,
        GameSettings settings,
        IReadOnlySet<string>? invalidDisputeIds)
    {
        var moderation = invalidDisputeIds != null
            ? new ModerationContext(invalidDisputeIds, [])
            : null;
        return ComputeRoundScores(round, settings, moderation);
    }

    public Dictionary<string, int> ComputeRoundScores(
        Round round,
        GameSettings settings,
        ModerationContext? moderation)
    {
        var scores = round.Answers.Keys.ToDictionary(playerId => playerId, _ => 0);

        // Build combined exclusion set: invalid disputes + host rejections
        var exclusions = moderation?.RejectedAnswerIds as IReadOnlySet<string> ?? new HashSet<string>();

        foreach (var category in round.Categories)
        {
            // Build merge substitution map for this category:
            // normalizedAnswer → "__merge__{groupId}" for answers that are part of a merge group
            var mergeSubstitutions = new Dictionary<string, string>();
            if (moderation != null)
            {
                foreach (var group in moderation.MergeGroups.Where(g => g.Category == category))
                {
                    foreach (var norm in group.MergedNormalizedAnswers)
                        mergeSubstitutions[norm] = $"__merge__{group.Id}";
                }
            }

            // Use pre-normalised answers (stored on submit); fall back to inline normalisation
            var answersByPlayer = round.Answers
                .Select(kv =>
                {
                    var norm = kv.Value.NormalizedAnswers.TryGetValue(category, out var n) ? n
                        : kv.Value.Answers.TryGetValue(category, out var raw) ? raw.Trim().ToLowerInvariant()
                        : string.Empty;
                    // Apply merge substitution
                    if (!string.IsNullOrWhiteSpace(norm) && mergeSubstitutions.TryGetValue(norm, out var substituted))
                        norm = substituted;
                    return (playerId: kv.Key, norm);
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.norm)
                    && !exclusions.Contains($"{category}:{x.norm}"))
                .ToDictionary(x => x.playerId, x => x.norm);

            if (answersByPlayer.Count == 0)
                continue;

            // Group players by their (possibly substituted) answer to find unique vs shared
            var answerGroups = answersByPlayer.GroupBy(kv => kv.Value).ToList();

            foreach (var group in answerGroups)
            {
                int points = group.Count() == 1 ? settings.UniqueAnswerPoints : settings.SharedAnswerPoints;
                foreach (var kv in group)
                    scores[kv.Key] += points;
            }
        }

        return scores;
    }
}
