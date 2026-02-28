namespace CategoriesBackend.Core.Models;

public class Player
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public bool IsGuest { get; set; }
    public bool IsConnected { get; set; }
    public int TotalScore { get; set; }
    public int BestAnswerVotes { get; set; }
}
