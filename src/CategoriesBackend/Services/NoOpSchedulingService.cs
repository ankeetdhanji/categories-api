using CategoriesBackend.Core.Interfaces;
using CategoriesBackend.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace CategoriesBackend.Services;

/// <summary>
/// Local-dev scheduling service: runs callbacks as background tasks using
/// IServiceScopeFactory so Scoped services are resolved correctly.
/// </summary>
public class NoOpSchedulingService(
    IServiceScopeFactory scopeFactory,
    IHubContext<GameHub> hub) : ISchedulingService
{
    public Task ScheduleBeginRoundAsync(string gameId, string sessionId, TimeSpan delay, CancellationToken ct = default)
    {
        _ = RunAfterDelayAsync(gameId, sessionId, delay, BeginRoundCallbackAsync);
        return Task.CompletedTask;
    }

    public Task ScheduleRoundEndAsync(string gameId, string sessionId, TimeSpan delay, CancellationToken ct = default)
    {
        _ = RunAfterDelayAsync(gameId, sessionId, delay, EndRoundCallbackAsync);
        return Task.CompletedTask;
    }

    public Task ScheduleNextRoundAsync(string gameId, string sessionId, TimeSpan delay, CancellationToken ct = default)
    {
        _ = RunAfterDelayAsync(gameId, sessionId, delay, BeginNextRoundCallbackAsync);
        return Task.CompletedTask;
    }

    public Task ScheduleDisputeCloseAsync(string gameId, string sessionId, string disputeId, TimeSpan delay, CancellationToken ct = default)
        => Task.CompletedTask; // Not implemented for local dev

    public Task ScheduleHostTransferAsync(string gameId, string sessionId, int delaySeconds, CancellationToken ct = default)
    {
        _ = RunHostTransferAsync(gameId, sessionId, TimeSpan.FromSeconds(delaySeconds));
        return Task.CompletedTask;
    }

    public Task CancelScheduledTaskAsync(string taskName, CancellationToken ct = default)
        => Task.CompletedTask;

    private async Task RunAfterDelayAsync(string gameId, string sessionId, TimeSpan delay, Func<string, string, IServiceScope, Task> callback)
    {
        await Task.Delay(delay);
        using var scope = scopeFactory.CreateScope();
        try
        {
            var gameManager = scope.ServiceProvider.GetRequiredService<IGameManager>();
            var game = await gameManager.GetGameAsync(gameId);
            if (!string.IsNullOrEmpty(sessionId) && game.SessionId != sessionId) return; // stale task
            await callback(gameId, sessionId, scope);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[NoOpSchedulingService] Background callback failed for game {gameId}: {ex}");
        }
    }

    private async Task RunHostTransferAsync(string gameId, string sessionId, TimeSpan delay)
    {
        await Task.Delay(delay);
        using var scope = scopeFactory.CreateScope();
        try
        {
            var gameManager = scope.ServiceProvider.GetRequiredService<IGameManager>();
            var game = await gameManager.GetGameAsync(gameId);
            if (!string.IsNullOrEmpty(sessionId) && game.SessionId != sessionId) return; // stale
            if (!game.IsAwaitingHost) return; // host reconnected

            var newHostId = await gameManager.TransferHostAsync(gameId, game.HostPlayerId);
            await gameManager.ResolveHostAwaitAsync(gameId);

            if (newHostId != null)
            {
                await hub.Clients.Group(gameId).SendAsync(GameHubEvents.HostChanged, new { hostPlayerId = newHostId });
            }
            else
            {
                await gameManager.MarkGameAbandonedAsync(gameId);
                await hub.Clients.Group(gameId).SendAsync(GameHubEvents.GameAbandoned, new { gameId });
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[NoOpSchedulingService] HostTransfer failed for game {gameId}: {ex}");
        }
    }

    private async Task BeginRoundCallbackAsync(string gameId, string sessionId, IServiceScope scope)
    {
        var gameManager = scope.ServiceProvider.GetRequiredService<IGameManager>();
        var schedulingService = scope.ServiceProvider.GetRequiredService<ISchedulingService>();

        var round = await gameManager.BeginRoundAsync(gameId);
        var game = await gameManager.GetGameAsync(gameId);

        await hub.Clients.Group(gameId).SendAsync(GameHubEvents.RoundStarted, new
        {
            roundNumber = round.RoundNumber,
            letter = round.Letter.ToString(),
            categories = round.Categories,
            startedAt = round.StartedAt,
            endsAt = round.EndedAt,
        });

        if (round.EndedAt.HasValue)
        {
            var roundDelay = round.EndedAt.Value - DateTimeOffset.UtcNow;
            await schedulingService.ScheduleRoundEndAsync(gameId, game.SessionId, roundDelay > TimeSpan.Zero ? roundDelay : TimeSpan.Zero);
        }
    }

    private async Task EndRoundCallbackAsync(string gameId, string sessionId, IServiceScope scope)
    {
        var roundManager = scope.ServiceProvider.GetRequiredService<IRoundManager>();
        var disputeManager = scope.ServiceProvider.GetRequiredService<IDisputeManager>();
        var gameManager = scope.ServiceProvider.GetRequiredService<IGameManager>();
        var schedulingService = scope.ServiceProvider.GetRequiredService<ISchedulingService>();

        var game = await gameManager.GetGameAsync(gameId);
        var currentRound = game.Rounds[game.CurrentRoundIndex];

        var actuallyEnded = await roundManager.EndRoundAsync(gameId);
        if (!actuallyEnded) return; // already ended — don't re-broadcast stale events

        await hub.Clients.Group(gameId).SendAsync(GameHubEvents.RoundEnded, new { roundNumber = currentRound.RoundNumber });

        var scoreResult = await roundManager.ScoreRoundAsync(gameId);
        await hub.Clients.Group(gameId).SendAsync(GameHubEvents.LeaderboardUpdated, new
        {
            roundNumber = scoreResult.RoundNumber,
            roundScores = scoreResult.RoundScores,
            leaderboard = scoreResult.Leaderboard,
        });

        var disputes = await disputeManager.DetectDisputesAsync(gameId);
        if (disputes.Count > 0)
        {
            await hub.Clients.Group(gameId).SendAsync(GameHubEvents.DisputeFlagged, new
            {
                roundNumber = currentRound.RoundNumber,
                disputes = disputes.Select(d => new { id = d.Id, category = d.Category, playerId = d.PlayerId, rawAnswer = d.RawAnswer }),
            });
        }
    }

    private async Task BeginNextRoundCallbackAsync(string gameId, string sessionId, IServiceScope scope)
    {
        var gameManager = scope.ServiceProvider.GetRequiredService<IGameManager>();
        var schedulingService = scope.ServiceProvider.GetRequiredService<ISchedulingService>();

        var round = await gameManager.BeginNextRoundAsync(gameId);
        if (round == null) return; // game over — host finalizes manually

        var game = await gameManager.GetGameAsync(gameId);

        await hub.Clients.Group(gameId).SendAsync(GameHubEvents.RoundStarted, new
        {
            roundNumber = round.RoundNumber,
            letter = round.Letter.ToString(),
            categories = round.Categories,
            startedAt = round.StartedAt,
            endsAt = round.EndedAt,
        });

        if (round.EndedAt.HasValue)
        {
            var roundDelay = round.EndedAt.Value - DateTimeOffset.UtcNow;
            await schedulingService.ScheduleRoundEndAsync(gameId, game.SessionId, roundDelay > TimeSpan.Zero ? roundDelay : TimeSpan.Zero);
        }
    }
}
