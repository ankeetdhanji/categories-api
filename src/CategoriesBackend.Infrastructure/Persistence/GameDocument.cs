using CategoriesBackend.Core.Enums;
using CategoriesBackend.Core.Models;
using Google.Cloud.Firestore;
using DisputeStatus = CategoriesBackend.Core.Models.DisputeStatus;

namespace CategoriesBackend.Infrastructure.Persistence;

/// <summary>
/// Firestore-annotated representation of a Game.
/// Keeps Firestore dependencies out of the Core domain models.
/// </summary>
[FirestoreData]
internal class GameDocument
{
    [FirestoreProperty] public string Id { get; set; } = string.Empty;
    [FirestoreProperty] public string JoinCode { get; set; } = string.Empty;
    [FirestoreProperty] public string HostPlayerId { get; set; } = string.Empty;
    [FirestoreProperty] public string Status { get; set; } = nameof(GameStatus.Lobby);
    [FirestoreProperty] public List<PlayerDocument> Players { get; set; } = [];
    [FirestoreProperty] public List<RoundDocument> Rounds { get; set; } = [];
    [FirestoreProperty] public GameSettingsDocument Settings { get; set; } = new();
    [FirestoreProperty] public int CurrentRoundIndex { get; set; } = -1;
    [FirestoreProperty] public Timestamp CreatedAt { get; set; }

    public static GameDocument FromGame(Game game) => new()
    {
        Id = game.Id,
        JoinCode = game.JoinCode,
        HostPlayerId = game.HostPlayerId,
        Status = game.Status.ToString(),
        Players = game.Players.Select(PlayerDocument.FromPlayer).ToList(),
        Rounds = game.Rounds.Select(RoundDocument.FromRound).ToList(),
        Settings = GameSettingsDocument.FromSettings(game.Settings),
        CurrentRoundIndex = game.CurrentRoundIndex,
        CreatedAt = Timestamp.FromDateTimeOffset(game.CreatedAt),
    };

    public Game ToGame() => new()
    {
        Id = Id,
        JoinCode = JoinCode,
        HostPlayerId = HostPlayerId,
        Status = Enum.TryParse<GameStatus>(Status, out var s) ? s : GameStatus.Lobby,
        Players = Players.Select(p => p.ToPlayer()).ToList(),
        Rounds = Rounds.Select(r => r.ToRound()).ToList(),
        Settings = Settings.ToSettings(),
        CurrentRoundIndex = CurrentRoundIndex,
        CreatedAt = CreatedAt.ToDateTimeOffset(),
    };
}

[FirestoreData]
internal class PlayerDocument
{
    [FirestoreProperty] public string Id { get; set; } = string.Empty;
    [FirestoreProperty] public string DisplayName { get; set; } = string.Empty;
    [FirestoreProperty] public string? AvatarUrl { get; set; }
    [FirestoreProperty] public bool IsGuest { get; set; }
    [FirestoreProperty] public bool IsConnected { get; set; }
    [FirestoreProperty] public int TotalScore { get; set; }
    [FirestoreProperty] public int BestAnswerVotes { get; set; }

    public static PlayerDocument FromPlayer(Player p) => new()
    {
        Id = p.Id,
        DisplayName = p.DisplayName,
        AvatarUrl = p.AvatarUrl,
        IsGuest = p.IsGuest,
        IsConnected = p.IsConnected,
        TotalScore = p.TotalScore,
        BestAnswerVotes = p.BestAnswerVotes,
    };

    public Player ToPlayer() => new()
    {
        Id = Id,
        DisplayName = DisplayName,
        AvatarUrl = AvatarUrl,
        IsGuest = IsGuest,
        IsConnected = IsConnected,
        TotalScore = TotalScore,
        BestAnswerVotes = BestAnswerVotes,
    };
}

[FirestoreData]
internal class GameSettingsDocument
{
    [FirestoreProperty] public bool IsTimedMode { get; set; } = true;
    [FirestoreProperty] public int RoundDurationSeconds { get; set; } = 60;
    [FirestoreProperty] public int MaxRounds { get; set; } = 5;
    [FirestoreProperty] public int MaxPlayers { get; set; } = 10;
    [FirestoreProperty] public int UniqueAnswerPoints { get; set; } = 10;
    [FirestoreProperty] public int SharedAnswerPoints { get; set; } = 5;
    [FirestoreProperty] public int BestAnswerBonusPoints { get; set; } = 20;
    [FirestoreProperty] public int DisputeVotingWindowSeconds { get; set; } = 30;
    [FirestoreProperty] public List<string> Categories { get; set; } = [];

    public static GameSettingsDocument FromSettings(GameSettings s) => new()
    {
        IsTimedMode = s.IsTimedMode,
        RoundDurationSeconds = s.RoundDurationSeconds,
        MaxRounds = s.MaxRounds,
        MaxPlayers = s.MaxPlayers,
        UniqueAnswerPoints = s.UniqueAnswerPoints,
        SharedAnswerPoints = s.SharedAnswerPoints,
        BestAnswerBonusPoints = s.BestAnswerBonusPoints,
        DisputeVotingWindowSeconds = s.DisputeVotingWindowSeconds,
        Categories = [..s.Categories],
    };

    public GameSettings ToSettings() => new()
    {
        IsTimedMode = IsTimedMode,
        RoundDurationSeconds = RoundDurationSeconds,
        MaxRounds = MaxRounds,
        MaxPlayers = MaxPlayers,
        UniqueAnswerPoints = UniqueAnswerPoints,
        SharedAnswerPoints = SharedAnswerPoints,
        BestAnswerBonusPoints = BestAnswerBonusPoints,
        DisputeVotingWindowSeconds = DisputeVotingWindowSeconds,
        Categories = [..Categories],
    };
}

[FirestoreData]
internal class RoundDocument
{
    [FirestoreProperty] public int RoundNumber { get; set; }
    [FirestoreProperty] public string Letter { get; set; } = string.Empty;
    [FirestoreProperty] public List<string> Categories { get; set; } = [];
    [FirestoreProperty] public string Status { get; set; } = nameof(RoundStatus.NotStarted);
    [FirestoreProperty] public Timestamp StartedAt { get; set; }
    [FirestoreProperty] public Timestamp EndedAt { get; set; }
    [FirestoreProperty] public bool HasStartedAt { get; set; }
    [FirestoreProperty] public bool HasEndedAt { get; set; }
    [FirestoreProperty] public Dictionary<string, PlayerAnswersDocument> Answers { get; set; } = [];
    [FirestoreProperty] public Dictionary<string, int> RoundScores { get; set; } = [];
    [FirestoreProperty] public List<DisputeDocument> Disputes { get; set; } = [];

    public static RoundDocument FromRound(Round r) => new()
    {
        RoundNumber = r.RoundNumber,
        Letter = r.Letter.ToString(),
        Categories = [.. r.Categories],
        Status = r.Status.ToString(),
        HasStartedAt = r.StartedAt.HasValue,
        StartedAt = r.StartedAt.HasValue ? Timestamp.FromDateTimeOffset(r.StartedAt.Value) : default,
        HasEndedAt = r.EndedAt.HasValue,
        EndedAt = r.EndedAt.HasValue ? Timestamp.FromDateTimeOffset(r.EndedAt.Value) : default,
        Answers = r.Answers.ToDictionary(kv => kv.Key, kv => PlayerAnswersDocument.From(kv.Value)),
        RoundScores = new Dictionary<string, int>(r.RoundScores),
        Disputes = r.Disputes.Select(DisputeDocument.From).ToList(),
    };

    public Round ToRound() => new()
    {
        RoundNumber = RoundNumber,
        Letter = Letter.Length > 0 ? Letter[0] : 'A',
        Categories = [.. Categories],
        Status = Enum.TryParse<RoundStatus>(Status, out var s) ? s : RoundStatus.NotStarted,
        StartedAt = HasStartedAt ? StartedAt.ToDateTimeOffset() : null,
        EndedAt = HasEndedAt ? EndedAt.ToDateTimeOffset() : null,
        Answers = Answers.ToDictionary(kv => kv.Key, kv => kv.Value.ToPlayerAnswers()),
        RoundScores = new Dictionary<string, int>(RoundScores),
        Disputes = Disputes.Select(d => d.ToDispute()).ToList(),
    };
}

[FirestoreData]
internal class PlayerAnswersDocument
{
    [FirestoreProperty] public string PlayerId { get; set; } = string.Empty;
    [FirestoreProperty] public Dictionary<string, string> Answers { get; set; } = [];
    [FirestoreProperty] public Dictionary<string, string> NormalizedAnswers { get; set; } = [];
    [FirestoreProperty] public bool IsSubmitted { get; set; }

    public static PlayerAnswersDocument From(PlayerAnswers pa) => new()
    {
        PlayerId = pa.PlayerId,
        Answers = new Dictionary<string, string>(pa.Answers),
        NormalizedAnswers = new Dictionary<string, string>(pa.NormalizedAnswers),
        IsSubmitted = pa.IsSubmitted,
    };

    public PlayerAnswers ToPlayerAnswers() => new()
    {
        PlayerId = PlayerId,
        Answers = new Dictionary<string, string>(Answers),
        NormalizedAnswers = new Dictionary<string, string>(NormalizedAnswers),
        IsSubmitted = IsSubmitted,
    };
}

[FirestoreData]
internal class DisputeDocument
{
    [FirestoreProperty] public string Id { get; set; } = string.Empty;
    [FirestoreProperty] public string Category { get; set; } = string.Empty;
    [FirestoreProperty] public string PlayerId { get; set; } = string.Empty;
    [FirestoreProperty] public string RawAnswer { get; set; } = string.Empty;
    [FirestoreProperty] public string NormalizedAnswer { get; set; } = string.Empty;
    [FirestoreProperty] public string Status { get; set; } = nameof(DisputeStatus.Pending);

    public static DisputeDocument From(Dispute d) => new()
    {
        Id = d.Id,
        Category = d.Category,
        PlayerId = d.PlayerId,
        RawAnswer = d.RawAnswer,
        NormalizedAnswer = d.NormalizedAnswer,
        Status = d.Status.ToString(),
    };

    public Dispute ToDispute() => new()
    {
        Id = Id,
        Category = Category,
        PlayerId = PlayerId,
        RawAnswer = RawAnswer,
        NormalizedAnswer = NormalizedAnswer,
        Status = Enum.TryParse<DisputeStatus>(Status, out var s) ? s : DisputeStatus.Pending,
    };
}
