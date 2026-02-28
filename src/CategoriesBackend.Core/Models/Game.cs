using CategoriesBackend.Core.Enums;

namespace CategoriesBackend.Core.Models;

public class Game
{
    public string Id { get; set; } = string.Empty;
    public string JoinCode { get; set; } = string.Empty;
    public string HostPlayerId { get; set; } = string.Empty;
    public GameStatus Status { get; set; } = GameStatus.Lobby;
    public List<Player> Players { get; set; } = [];
    public List<Round> Rounds { get; set; } = [];
    public GameSettings Settings { get; set; } = new();
    public int CurrentRoundIndex { get; set; } = -1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
