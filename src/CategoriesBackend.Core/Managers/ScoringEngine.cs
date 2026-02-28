using CategoriesBackend.Core.Interfaces;
using CategoriesBackend.Core.Models;

namespace CategoriesBackend.Core.Managers;

public class ScoringEngine : IScoringEngine
{
    public Dictionary<string, int> ComputeRoundScores(Round round, GameSettings settings)
    {
        var scores = round.Answers.Keys.ToDictionary(playerId => playerId, _ => 0);

        foreach (var category in round.Categories)
        {
            // Gather all non-empty answers for this category, normalised to lowercase
            var answersByPlayer = round.Answers
                .Where(kv => kv.Value.Answers.TryGetValue(category, out var ans) && !string.IsNullOrWhiteSpace(ans))
                .ToDictionary(kv => kv.Key, kv => kv.Value.Answers[category].Trim().ToLowerInvariant());

            if (answersByPlayer.Count == 0)
                continue;

            // Group players by their answer to find unique vs shared
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
