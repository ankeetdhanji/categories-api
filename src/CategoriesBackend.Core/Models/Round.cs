namespace CategoriesBackend.Core.Models;

public class Round
{
    public int RoundNumber { get; set; }
    public char Letter { get; set; }
    public List<string> Categories { get; set; } = [];
    public Dictionary<string, PlayerAnswers> Answers { get; set; } = []; // keyed by playerId
    public Dictionary<string, int> RoundScores { get; set; } = [];       // playerId → points earned this round
    public List<Dispute> Disputes { get; set; } = [];
    public RoundStatus Status { get; set; } = RoundStatus.NotStarted;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
}

public class Dispute
{
    /// <summary>Deterministic ID: "{category}:{normalizedAnswer}" — shared across players with the same wrong answer.</summary>
    public string Id { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string PlayerId { get; set; } = string.Empty;
    public string RawAnswer { get; set; } = string.Empty;
    public string NormalizedAnswer { get; set; } = string.Empty;
    public DisputeStatus Status { get; set; } = DisputeStatus.Pending;
}

public enum DisputeStatus { Pending, Valid, Invalid }

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
    public Dictionary<string, string> Answers { get; set; } = [];           // category → raw answer
    public Dictionary<string, string> NormalizedAnswers { get; set; } = []; // category → trimmed lowercase
    public bool IsSubmitted { get; set; }
}
