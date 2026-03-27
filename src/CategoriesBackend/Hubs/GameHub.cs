using CategoriesBackend.Core.Enums;
using CategoriesBackend.Core.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace CategoriesBackend.Hubs;

/// <summary>
/// SignalR hub for real-time game events.
/// REST = commands, SignalR = events.
/// </summary>
public class GameHub(
    IPlayerConnectionTracker tracker,
    IGameManager gameManager,
    IRoundManager roundManager,
    IDisputeManager disputeManager,
    ISchedulingService schedulingService,
    IHubContext<GameHub> hubContext) : Hub
{
    private const int HostGraceWindowSeconds = 90;

    // --- Client → Server: group management ---

    public async Task JoinGameGroup(string gameId, string playerId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, gameId);
        tracker.Register(Context.ConnectionId, gameId, playerId);
        await gameManager.SetPlayerConnectedAsync(gameId, playerId, isConnected: true);
    }

    public async Task LeaveGameGroup(string gameId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, gameId);
        tracker.Unregister(Context.ConnectionId);
    }

    public async Task SendReaction(string gameId, string emoji)
    {
        await Clients.OthersInGroup(gameId).SendAsync(GameHubEvents.EmojiReaction, new { emoji });
    }

    public async Task NotifyAnswerPresence(string gameId, string playerId, string category, bool hasAnswer)
    {
        await Clients.OthersInGroup(gameId).SendAsync(
            GameHubEvents.PlayerAnswerUpdated,
            new { playerId, category, hasAnswer });
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var info = tracker.Get(Context.ConnectionId);
        if (info != null)
        {
            var (gameId, playerId) = info.Value;
            tracker.Unregister(Context.ConnectionId);

            await gameManager.SetPlayerConnectedAsync(gameId, playerId, isConnected: false);

            await hubContext.Clients.Group(gameId).SendAsync(GameHubEvents.PlayerLeft, new { playerId });

            var game = await gameManager.GetGameAsync(gameId);

            // T1-A: In relaxed mode, if this player's disconnect means all remaining players are done,
            // trigger the end-round cascade so the round isn't permanently blocked.
            if (game.Status == GameStatus.InRound && !game.Settings.IsTimedMode)
            {
                _ = TryTriggerRelaxedRoundEndAsync(gameId, playerId);
            }

            if (game.HostPlayerId == playerId && game.Status != GameStatus.Finished)
            {
                _ = ScheduleHostTransferAsync(gameId, playerId);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task TryTriggerRelaxedRoundEndAsync(string gameId, string disconnectedPlayerId)
    {
        try
        {
            var allDone = await roundManager.MarkPlayerDoneAsync(gameId, disconnectedPlayerId);
            if (!allDone) return;

            var game = await gameManager.GetGameAsync(gameId);
            var currentRound = game.Rounds[game.CurrentRoundIndex];

            await RoundEndCascade.ExecuteAsync(
                gameId,
                currentRound.RoundNumber,
                roundManager,
                disputeManager,
                schedulingService,
                hubContext);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[GameHub] TryTriggerRelaxedRoundEndAsync failed for game {gameId}: {ex.Message}");
        }
    }

    private async Task ScheduleHostTransferAsync(string gameId, string disconnectedHostId)
    {
        await Task.Delay(TimeSpan.FromSeconds(HostGraceWindowSeconds));

        var newHostId = await gameManager.TransferHostAsync(gameId, disconnectedHostId);
        if (newHostId != null)
        {
            await hubContext.Clients.Group(gameId).SendAsync(
                GameHubEvents.HostChanged,
                new { hostPlayerId = newHostId });
        }
    }
}

// --- Server → Client event contracts (for documentation / type-safety) ---

public static class GameHubEvents
{
    // Lobby
    public const string PlayerJoined = "PlayerJoined";
    public const string PlayerLeft = "PlayerLeft";
    public const string SettingsUpdated = "SettingsUpdated";
    public const string HostChanged = "HostChanged";

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
    public const string PlayerDone = "PlayerDone";

    // Review phase
    public const string CategoryAdvanced = "CategoryAdvanced";
    public const string DisputeVoteUpdated = "DisputeVoteUpdated";
    public const string ReviewComplete = "ReviewComplete";

    // Reactions
    public const string EmojiReaction = "EmojiReaction";

    // Answer presence (for avatar badges)
    public const string PlayerAnswerUpdated = "PlayerAnswerUpdated";
}
