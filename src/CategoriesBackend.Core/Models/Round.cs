namespace CategoriesBackend.Core.Models;

public class Round
{
    public int RoundNumber { get; set; }
    public char Letter { get; set; }
    public List<string> Categories { get; set; } = [];
    public Dictionary<string, PlayerAnswers> Answers { get; set; } = []; // keyed by playerId
    public RoundStatus Status { get; set; } = RoundStatus.NotStarted;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
}

public enum RoundStatus
{
    NotStarted,
    Answering,
    Locked,
    Results,
    Disputes,
    BestAnswerVoting,
    Complete
}

public class PlayerAnswers
{
    public string PlayerId { get; set; } = string.Empty;
    public Dictionary<string, string> Answers { get; set; } = []; // category → answer
    public bool IsSubmitted { get; set; }
}
