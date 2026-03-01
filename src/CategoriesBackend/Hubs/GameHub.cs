using Microsoft.AspNetCore.SignalR;

namespace CategoriesBackend.Hubs;

/// <summary>
/// SignalR hub for real-time game events.
/// REST = commands, SignalR = events.
/// </summary>
public class GameHub : Hub
{
    // --- Client → Server: group management ---

    public async Task JoinGameGroup(string gameId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, gameId);
    }

    public async Task LeaveGameGroup(string gameId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, gameId);
    }

    public async Task SendReaction(string gameId, string emoji)
    {
        await Clients.OthersInGroup(gameId).SendAsync(GameHubEvents.EmojiReaction, new { emoji });
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // TODO: notify game manager of player disconnect; trigger host-transfer grace window if host
        await base.OnDisconnectedAsync(exception);
    }
}

// --- Server → Client event contracts (for documentation / type-safety) ---

public static class GameHubEvents
{
    // Lobby
    public const string PlayerJoined = "PlayerJoined";
    public const string PlayerLeft = "PlayerLeft";
    public const string SettingsUpdated = "SettingsUpdated";

    // Game lifecycle
    public const string GameCountdown = "GameCountdown";
    public const string RoundStarted = "RoundStarted";
    public const string RoundEnded = "RoundEnded";
    public const string PhaseChanged = "PhaseChanged";

    // Results & disputes
    public const string DisputeFlagged = "DisputeFlagged";
    public const string DisputeResolved = "DisputeResolved";
    public const string LeaderboardUpdated = "LeaderboardUpdated";

    // Round activity
    public const string PlayerSubmitted = "PlayerSubmitted";

    // Reactions
    public const string EmojiReaction = "EmojiReaction";
}
