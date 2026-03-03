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
}
