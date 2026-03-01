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
            // Use pre-normalised answers (stored on submit); fall back to inline normalisation
            var answersByPlayer = round.Answers
                .Select(kv =>
                {
                    var norm = kv.Value.NormalizedAnswers.TryGetValue(category, out var n) ? n
                        : kv.Value.Answers.TryGetValue(category, out var raw) ? raw.Trim().ToLowerInvariant()
                        : string.Empty;
                    return (playerId: kv.Key, norm);
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.norm))
                .ToDictionary(x => x.playerId, x => x.norm);

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
