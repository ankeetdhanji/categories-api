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
    public Task ScheduleBeginRoundAsync(string gameId, TimeSpan delay, CancellationToken ct = default)
    {
        _ = RunAfterDelayAsync(gameId, delay, BeginRoundCallbackAsync);
        return Task.CompletedTask;
    }

    public Task ScheduleRoundEndAsync(string gameId, TimeSpan delay, CancellationToken ct = default)
    {
        _ = RunAfterDelayAsync(gameId, delay, EndRoundCallbackAsync);
        return Task.CompletedTask;
    }

    public Task ScheduleNextRoundAsync(string gameId, TimeSpan delay, CancellationToken ct = default)
    {
        _ = RunAfterDelayAsync(gameId, delay, BeginNextRoundCallbackAsync);
        return Task.CompletedTask;
    }

    public Task ScheduleDisputeCloseAsync(string gameId, string disputeId, TimeSpan delay, CancellationToken ct = default)
        => Task.CompletedTask; // Not implemented for local dev

    public Task CancelScheduledTaskAsync(string taskName, CancellationToken ct = default)
        => Task.CompletedTask;

    private async Task RunAfterDelayAsync(string gameId, TimeSpan delay, Func<string, IServiceScope, Task> callback)
    {
        await Task.Delay(delay);
        using var scope = scopeFactory.CreateScope();
        try { await callback(gameId, scope); }
        catch { /* swallow — background task */ }
    }

    private async Task BeginRoundCallbackAsync(string gameId, IServiceScope scope)
    {
        var gameManager = scope.ServiceProvider.GetRequiredService<IGameManager>();
        var schedulingService = scope.ServiceProvider.GetRequiredService<ISchedulingService>();

        var round = await gameManager.BeginRoundAsync(gameId);
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
            await schedulingService.ScheduleRoundEndAsync(gameId, roundDelay > TimeSpan.Zero ? roundDelay : TimeSpan.Zero);
        }
    }

    private async Task EndRoundCallbackAsync(string gameId, IServiceScope scope)
    {
        var roundManager = scope.ServiceProvider.GetRequiredService<IRoundManager>();
        var disputeManager = scope.ServiceProvider.GetRequiredService<IDisputeManager>();
        var gameManager = scope.ServiceProvider.GetRequiredService<IGameManager>();
        var schedulingService = scope.ServiceProvider.GetRequiredService<ISchedulingService>();

        var game = await gameManager.GetGameAsync(gameId);
        var currentRound = game.Rounds[game.CurrentRoundIndex];

        await roundManager.EndRoundAsync(gameId);
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

        // Schedule next round if rounds remain
        var freshGame = await gameManager.GetGameAsync(gameId);
        if (freshGame.CurrentRoundIndex + 1 < freshGame.Rounds.Count)
            await schedulingService.ScheduleNextRoundAsync(gameId, TimeSpan.FromSeconds(3));
    }

    private async Task BeginNextRoundCallbackAsync(string gameId, IServiceScope scope)
    {
        var gameManager = scope.ServiceProvider.GetRequiredService<IGameManager>();
        var schedulingService = scope.ServiceProvider.GetRequiredService<ISchedulingService>();

        var round = await gameManager.BeginNextRoundAsync(gameId);
        if (round == null) return; // game over — host finalizes manually

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
            await schedulingService.ScheduleRoundEndAsync(gameId, roundDelay > TimeSpan.Zero ? roundDelay : TimeSpan.Zero);
        }
    }
}
