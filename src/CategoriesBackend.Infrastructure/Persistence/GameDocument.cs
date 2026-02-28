using CategoriesBackend.Core.Enums;
using CategoriesBackend.Core.Models;
using Google.Cloud.Firestore;

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
