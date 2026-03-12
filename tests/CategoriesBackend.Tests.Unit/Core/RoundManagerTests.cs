using CategoriesBackend.Core.Enums;
using CategoriesBackend.Core.Interfaces;
using CategoriesBackend.Core.Managers;
using CategoriesBackend.Core.Models;
using NSubstitute;

namespace CategoriesBackend.Tests.Unit.Core;

public class RoundManagerTests
{
    private readonly IGameRepository _repo = Substitute.For<IGameRepository>();
    private readonly IScoringEngine _scoringEngine = Substitute.For<IScoringEngine>();
    private readonly RoundManager _sut;

    public RoundManagerTests()
    {
        _sut = new RoundManager(_repo, _scoringEngine);
    }

    // --- Helpers ---

    private static Game GameWithActiveRound(Dictionary<string, int>? existingRoundScores = null)
    {
        var round = new Round
        {
            RoundNumber = 1,
            Letter = 'A',
            Categories = ["Animal"],
            Status = RoundStatus.Locked,
            RoundScores = existingRoundScores ?? [],
        };

        return new Game
        {
            Id = "game-1",
            Status = GameStatus.InRound,
            CurrentRoundIndex = 0,
            Rounds = [round],
            Players =
            [
                new Player { Id = "p1", DisplayName = "Alice", TotalScore = 0 },
                new Player { Id = "p2", DisplayName = "Bob", TotalScore = 5 },
            ],
            Settings = new GameSettings(),
        };
    }

    // --- ScoreRoundAsync ---

    [Fact]
    public async Task ScoreRound_StoresRoundScores_OnRound()
    {
        var game = GameWithActiveRound();
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);
        _scoringEngine.ComputeRoundScores(Arg.Any<Round>(), Arg.Any<GameSettings>())
            .Returns(new Dictionary<string, int> { ["p1"] = 10, ["p2"] = 5 });

        await _sut.ScoreRoundAsync("game-1");

        Assert.Equal(10, game.Rounds[0].RoundScores["p1"]);
        Assert.Equal(5, game.Rounds[0].RoundScores["p2"]);
    }

    [Fact]
    public async Task ScoreRound_AddsRoundPointsToPlayerTotals()
    {
        var game = GameWithActiveRound();
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);
        _scoringEngine.ComputeRoundScores(Arg.Any<Round>(), Arg.Any<GameSettings>())
            .Returns(new Dictionary<string, int> { ["p1"] = 10, ["p2"] = 5 });

        await _sut.ScoreRoundAsync("game-1");

        // p1: 0 existing + 10 round = 10
        Assert.Equal(10, game.Players.Single(p => p.Id == "p1").TotalScore);
        // p2: 5 existing + 5 round = 10
        Assert.Equal(10, game.Players.Single(p => p.Id == "p2").TotalScore);
    }

    [Fact]
    public async Task ScoreRound_ReturnsLeaderboard_SortedByTotalScoreDescending()
    {
        var game = GameWithActiveRound();
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);
        _scoringEngine.ComputeRoundScores(Arg.Any<Round>(), Arg.Any<GameSettings>())
            .Returns(new Dictionary<string, int> { ["p1"] = 0, ["p2"] = 10 });

        var result = await _sut.ScoreRoundAsync("game-1");

        // p2: 5 existing + 10 round = 15 → first
        // p1: 0 existing + 0 round  = 0  → second
        Assert.Equal("p2", result.Leaderboard[0].PlayerId);
        Assert.Equal("p1", result.Leaderboard[1].PlayerId);
    }

    [Fact]
    public async Task ScoreRound_LeaderboardEntry_ContainsCorrectTotalAndRoundScores()
    {
        var game = GameWithActiveRound();
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);
        _scoringEngine.ComputeRoundScores(Arg.Any<Round>(), Arg.Any<GameSettings>())
            .Returns(new Dictionary<string, int> { ["p1"] = 10, ["p2"] = 5 });

        var result = await _sut.ScoreRoundAsync("game-1");

        var p1 = result.Leaderboard.Single(e => e.PlayerId == "p1");
        Assert.Equal(10, p1.RoundScore);
        Assert.Equal(10, p1.TotalScore); // 0 + 10

        var p2 = result.Leaderboard.Single(e => e.PlayerId == "p2");
        Assert.Equal(5, p2.RoundScore);
        Assert.Equal(10, p2.TotalScore); // 5 + 5
    }

    [Fact]
    public async Task ScoreRound_SetsGameStatus_ToRoundResults()
    {
        var game = GameWithActiveRound();
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);
        _scoringEngine.ComputeRoundScores(Arg.Any<Round>(), Arg.Any<GameSettings>())
            .Returns(new Dictionary<string, int> { ["p1"] = 10 });

        await _sut.ScoreRoundAsync("game-1");

        Assert.Equal(GameStatus.RoundResults, game.Status);
    }

    [Fact]
    public async Task ScoreRound_CallsSaveAsync_OnRepository()
    {
        var game = GameWithActiveRound();
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);
        _scoringEngine.ComputeRoundScores(Arg.Any<Round>(), Arg.Any<GameSettings>())
            .Returns(new Dictionary<string, int> { ["p1"] = 10 });

        await _sut.ScoreRoundAsync("game-1");

        await _repo.Received(1).SaveAsync(Arg.Any<Game>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScoreRound_WhenAlreadyScored_ReturnsExistingResult_WithoutReScoring()
    {
        // Pre-populate round scores to simulate a race condition (timed + force-end)
        var game = GameWithActiveRound(existingRoundScores: new Dictionary<string, int> { ["p1"] = 10, ["p2"] = 5 });
        game.Players.Single(p => p.Id == "p1").TotalScore = 10;
        game.Players.Single(p => p.Id == "p2").TotalScore = 10;
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        var result = await _sut.ScoreRoundAsync("game-1");

        _scoringEngine.DidNotReceive().ComputeRoundScores(Arg.Any<Round>(), Arg.Any<GameSettings>());
        Assert.Equal(10, result.RoundScores["p1"]);
        Assert.Equal(5, result.RoundScores["p2"]);
    }

    [Fact]
    public async Task ScoreRound_WhenAlreadyScored_DoesNotCallSaveAsync()
    {
        var game = GameWithActiveRound(existingRoundScores: new Dictionary<string, int> { ["p1"] = 10 });
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        await _sut.ScoreRoundAsync("game-1");

        await _repo.DidNotReceive().SaveAsync(Arg.Any<Game>(), Arg.Any<CancellationToken>());
    }

    // --- SubmitAnswersAsync ---

    private static Game GameWithAnsweringRound(bool alreadyScored = false)
    {
        var round = new Round
        {
            RoundNumber = 1,
            Letter = 'A',
            Categories = ["Animal"],
            Status = RoundStatus.Answering,
            RoundScores = alreadyScored ? new Dictionary<string, int> { ["p1"] = 10 } : [],
        };

        return new Game { Id = "game-1", CurrentRoundIndex = 0, Rounds = [round] };
    }

    [Fact]
    public async Task SubmitAnswers_StoresRawAndNormalizedAnswers()
    {
        var game = GameWithAnsweringRound();
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        await _sut.SubmitAnswersAsync("game-1", "p1",
            new Dictionary<string, string> { ["Animal"] = "  ANT  " });

        var stored = game.Rounds[0].Answers["p1"];
        Assert.Equal("  ANT  ", stored.Answers["Animal"]);
        Assert.Equal("ant", stored.NormalizedAnswers["Animal"]);
        Assert.True(stored.IsSubmitted);
    }

    [Fact]
    public async Task SubmitAnswers_WhenRoundAlreadyScored_SkipsSilently()
    {
        var game = GameWithAnsweringRound(alreadyScored: true);
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        await _sut.SubmitAnswersAsync("game-1", "p2",
            new Dictionary<string, string> { ["Animal"] = "Ant" });

        await _repo.DidNotReceive().SaveAsync(Arg.Any<Game>(), Arg.Any<CancellationToken>());
        Assert.False(game.Rounds[0].Answers.ContainsKey("p2"));
    }

    // --- MarkPlayerDoneAsync ---

    private static Game GameWithRelaxedRound(List<Player>? players = null)
    {
        var round = new Round
        {
            RoundNumber = 1,
            Letter = 'B',
            Categories = ["Animal"],
            Status = RoundStatus.Answering,
        };

        return new Game
        {
            Id = "game-1",
            Status = GameStatus.InRound,
            CurrentRoundIndex = 0,
            Rounds = [round],
            Players = players ??
            [
                new Player { Id = "p1", DisplayName = "Alice", IsConnected = true },
                new Player { Id = "p2", DisplayName = "Bob",   IsConnected = true },
            ],
            Settings = new GameSettings(),
        };
    }

    [Fact]
    public async Task MarkPlayerDone_AddsDonePlayerId_ToRound()
    {
        var game = GameWithRelaxedRound();
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        await _sut.MarkPlayerDoneAsync("game-1", "p1");

        Assert.Contains("p1", game.Rounds[0].DonePlayerIds);
    }

    [Fact]
    public async Task MarkPlayerDone_ReturnsFalse_WhenNotAllPlayersAreDone()
    {
        var game = GameWithRelaxedRound();
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        var result = await _sut.MarkPlayerDoneAsync("game-1", "p1");

        Assert.False(result);
    }

    [Fact]
    public async Task MarkPlayerDone_ReturnsTrue_WhenAllConnectedPlayersDone()
    {
        var game = GameWithRelaxedRound();
        game.Rounds[0].DonePlayerIds.Add("p1"); // p1 already done
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        var result = await _sut.MarkPlayerDoneAsync("game-1", "p2");

        Assert.True(result);
    }

    [Fact]
    public async Task MarkPlayerDone_IgnoresDisconnectedPlayers_WhenCheckingAllDone()
    {
        var players = new List<Player>
        {
            new() { Id = "p1", DisplayName = "Alice", IsConnected = true },
            new() { Id = "p2", DisplayName = "Bob",   IsConnected = false }, // disconnected
        };
        var game = GameWithRelaxedRound(players);
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        // Only p1 marks done — p2 is disconnected and should be ignored
        var result = await _sut.MarkPlayerDoneAsync("game-1", "p1");

        Assert.True(result);
    }

    [Fact]
    public async Task MarkPlayerDone_IsIdempotent_DoesNotDuplicatePlayerId()
    {
        var game = GameWithRelaxedRound();
        game.Rounds[0].DonePlayerIds.Add("p1"); // already marked
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        await _sut.MarkPlayerDoneAsync("game-1", "p1");

        Assert.Single(game.Rounds[0].DonePlayerIds, id => id == "p1");
        await _repo.DidNotReceive().SaveAsync(Arg.Any<Game>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkPlayerDone_WhenRoundAlreadyLocked_ReturnsTrue_WithoutSaving()
    {
        var game = GameWithRelaxedRound();
        game.Rounds[0].Status = RoundStatus.Locked;
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        var result = await _sut.MarkPlayerDoneAsync("game-1", "p1");

        Assert.True(result);
        await _repo.DidNotReceive().SaveAsync(Arg.Any<Game>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkPlayerDone_CallsSaveAsync_WhenPlayerAddedToDoneList()
    {
        var game = GameWithRelaxedRound();
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        await _sut.MarkPlayerDoneAsync("game-1", "p1");

        await _repo.Received(1).SaveAsync(Arg.Any<Game>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkPlayerDone_ExcludesSpectatingPlayers_FromAllDoneCheck()
    {
        var players = new List<Player>
        {
            new() { Id = "p1", DisplayName = "Alice", IsConnected = true,  IsSpectating = false },
            new() { Id = "p2", DisplayName = "Bob",   IsConnected = true,  IsSpectating = true  },
        };
        var game = GameWithRelaxedRound(players);
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        // Only p1 (non-spectating) marks done
        var result = await _sut.MarkPlayerDoneAsync("game-1", "p1");

        Assert.True(result); // p2 is spectating — should not block round end
    }

    // --- ApplyDisputeCorrectionsAsync ---

    private static Game GameWithScoredRoundAndDisputes(
        Dictionary<string, int> roundScores,
        List<Dispute> disputes,
        List<Player>? players = null)
    {
        var round = new Round
        {
            RoundNumber = 1,
            Letter = 'A',
            Categories = ["Animal"],
            Status = RoundStatus.Locked,
            RoundScores = roundScores,
            Disputes = disputes,
        };

        return new Game
        {
            Id = "game-1",
            Status = GameStatus.InRound,
            CurrentRoundIndex = 0,
            Rounds = [round],
            Players = players ??
            [
                new Player { Id = "p1", DisplayName = "Alice", TotalScore = 10 },
                new Player { Id = "p2", DisplayName = "Bob",   TotalScore = 5  },
            ],
            Settings = new GameSettings(),
        };
    }

    [Fact]
    public async Task ApplyDisputeCorrections_NoInvalidDisputes_DoesNotCallSaveAsync()
    {
        var disputes = new List<Dispute>
        {
            new() { Id = "Animal:xyz", Status = DisputeStatus.Valid },
        };
        var game = GameWithScoredRoundAndDisputes(
            new Dictionary<string, int> { ["p1"] = 10, ["p2"] = 5 },
            disputes);
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        await _sut.ApplyDisputeCorrectionsAsync("game-1");

        await _repo.DidNotReceive().SaveAsync(Arg.Any<Game>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyDisputeCorrections_NoInvalidDisputes_ReturnsExistingScores()
    {
        var game = GameWithScoredRoundAndDisputes(
            new Dictionary<string, int> { ["p1"] = 10, ["p2"] = 5 },
            []);
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        var result = await _sut.ApplyDisputeCorrectionsAsync("game-1");

        Assert.Equal(10, result.RoundScores["p1"]);
        Assert.Equal(5,  result.RoundScores["p2"]);
    }

    [Fact]
    public async Task ApplyDisputeCorrections_InvalidDispute_AdjustsPlayerTotalScore()
    {
        var disputes = new List<Dispute>
        {
            new() { Id = "Animal:xyz", Status = DisputeStatus.Invalid },
        };
        var game = GameWithScoredRoundAndDisputes(
            new Dictionary<string, int> { ["p1"] = 10, ["p2"] = 5 },
            disputes);
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);
        // After correction p1 scores 0 (invalid answer), p2 stays at 5
        _scoringEngine.ComputeRoundScores(Arg.Any<Round>(), Arg.Any<GameSettings>(), Arg.Any<IReadOnlySet<string>>())
            .Returns(new Dictionary<string, int> { ["p1"] = 0, ["p2"] = 5 });

        await _sut.ApplyDisputeCorrectionsAsync("game-1");

        // p1: was 10 total, round was 10, corrected to 0 → total drops by 10 → 0
        Assert.Equal(0, game.Players.Single(p => p.Id == "p1").TotalScore);
        // p2 unchanged
        Assert.Equal(5, game.Players.Single(p => p.Id == "p2").TotalScore);
    }

    [Fact]
    public async Task ApplyDisputeCorrections_InvalidDispute_UpdatesRoundScores()
    {
        var disputes = new List<Dispute>
        {
            new() { Id = "Animal:xyz", Status = DisputeStatus.Invalid },
        };
        var game = GameWithScoredRoundAndDisputes(
            new Dictionary<string, int> { ["p1"] = 10, ["p2"] = 5 },
            disputes);
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);
        _scoringEngine.ComputeRoundScores(Arg.Any<Round>(), Arg.Any<GameSettings>(), Arg.Any<IReadOnlySet<string>>())
            .Returns(new Dictionary<string, int> { ["p1"] = 0, ["p2"] = 5 });

        await _sut.ApplyDisputeCorrectionsAsync("game-1");

        Assert.Equal(0, game.Rounds[0].RoundScores["p1"]);
        Assert.Equal(5, game.Rounds[0].RoundScores["p2"]);
    }

    [Fact]
    public async Task ApplyDisputeCorrections_InvalidDispute_CallsSaveAsyncOnce()
    {
        var disputes = new List<Dispute>
        {
            new() { Id = "Animal:xyz", Status = DisputeStatus.Invalid },
        };
        var game = GameWithScoredRoundAndDisputes(
            new Dictionary<string, int> { ["p1"] = 10 },
            disputes);
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);
        _scoringEngine.ComputeRoundScores(Arg.Any<Round>(), Arg.Any<GameSettings>(), Arg.Any<IReadOnlySet<string>>())
            .Returns(new Dictionary<string, int> { ["p1"] = 0 });

        await _sut.ApplyDisputeCorrectionsAsync("game-1");

        await _repo.Received(1).SaveAsync(Arg.Any<Game>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyDisputeCorrections_ReturnsLeaderboard_SortedByTotalScoreDescending()
    {
        var disputes = new List<Dispute>
        {
            new() { Id = "Animal:xyz", Status = DisputeStatus.Invalid },
        };
        var game = GameWithScoredRoundAndDisputes(
            new Dictionary<string, int> { ["p1"] = 10, ["p2"] = 5 },
            disputes);
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);
        // p1 corrected to 0, p2 stays 5
        _scoringEngine.ComputeRoundScores(Arg.Any<Round>(), Arg.Any<GameSettings>(), Arg.Any<IReadOnlySet<string>>())
            .Returns(new Dictionary<string, int> { ["p1"] = 0, ["p2"] = 5 });

        var result = await _sut.ApplyDisputeCorrectionsAsync("game-1");

        // p2 total 5 > p1 total 0
        Assert.Equal("p2", result.Leaderboard[0].PlayerId);
        Assert.Equal("p1", result.Leaderboard[1].PlayerId);
    }

    [Fact]
    public async Task ApplyDisputeCorrections_SpectatingPlayer_TotalScoreNotAdjusted()
    {
        var players = new List<Player>
        {
            new() { Id = "p1", DisplayName = "Alice", TotalScore = 10, IsSpectating = true },
            new() { Id = "p2", DisplayName = "Bob",   TotalScore = 5,  IsSpectating = false },
        };
        var disputes = new List<Dispute>
        {
            new() { Id = "Animal:xyz", Status = DisputeStatus.Invalid },
        };
        var game = GameWithScoredRoundAndDisputes(
            new Dictionary<string, int> { ["p1"] = 10, ["p2"] = 5 },
            disputes,
            players);
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);
        _scoringEngine.ComputeRoundScores(Arg.Any<Round>(), Arg.Any<GameSettings>(), Arg.Any<IReadOnlySet<string>>())
            .Returns(new Dictionary<string, int> { ["p1"] = 0, ["p2"] = 5 });

        await _sut.ApplyDisputeCorrectionsAsync("game-1");

        // p1 is spectating — TotalScore should NOT change
        Assert.Equal(10, game.Players.Single(p => p.Id == "p1").TotalScore);
        // p2 is active — unchanged (0 delta)
        Assert.Equal(5, game.Players.Single(p => p.Id == "p2").TotalScore);
    }

    // --- ScoreRoundAsync: spectating players ---

    [Fact]
    public async Task ScoreRound_SpectatingPlayer_DoesNotReceiveTotalScoreUpdate()
    {
        var game = GameWithActiveRound();
        game.Players[0].IsSpectating = true; // p1 is spectating
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);
        _scoringEngine.ComputeRoundScores(Arg.Any<Round>(), Arg.Any<GameSettings>())
            .Returns(new Dictionary<string, int> { ["p1"] = 10, ["p2"] = 5 });

        await _sut.ScoreRoundAsync("game-1");

        // p1 is spectating — total score should NOT increase
        Assert.Equal(0, game.Players.Single(p => p.Id == "p1").TotalScore);
        // p2 is active — total score should increase
        Assert.Equal(10, game.Players.Single(p => p.Id == "p2").TotalScore); // 5 existing + 5 round
    }
}
