namespace CategoriesBackend.Core.Models;

public class GameSettings
{
    public static readonly List<string> DefaultCategories =
    [
        "A boy's name", "A girl's name", "A country", "An animal",
        "A city", "A food", "A TV show", "Something you find at school",
    ];

    public bool IsTimedMode { get; set; } = true;
    public int RoundDurationSeconds { get; set; } = 60;
    public int MaxRounds { get; set; } = 5;
    public int MaxPlayers { get; set; } = 10;
    public int UniqueAnswerPoints { get; set; } = 10;
    public int SharedAnswerPoints { get; set; } = 5;
    public int BestAnswerBonusPoints { get; set; } = 20;
    public int DisputeVotingWindowSeconds { get; set; } = 30;
    public List<string> Categories { get; set; } = [..DefaultCategories];
}
