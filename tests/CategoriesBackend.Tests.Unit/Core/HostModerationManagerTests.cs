using CategoriesBackend.Core.Enums;
using CategoriesBackend.Core.Interfaces;
using CategoriesBackend.Core.Managers;
using CategoriesBackend.Core.Models;
using NSubstitute;

namespace CategoriesBackend.Tests.Unit.Core;

public class HostModerationManagerTests
{
    /// <summary>
    /// Concrete stub that executes RunInTransactionAsync lambdas in-process,
    /// avoiding NSubstitute's limitations with generic method matching.
    /// </summary>
    private sealed class FakeGameRepo : IGameRepository
    {
        public Game? Game { get; set; }

        public Task<Game?> GetByIdAsync(string gameId, CancellationToken ct = default) => Task.FromResult(Game);
        public Task<Game?> GetByJoinCodeAsync(string joinCode, CancellationToken ct = default) => Task.FromResult<Game?>(null);
        public Task SaveAsync(Game game, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> UpdateAnswersAsync(string gameId, int roundIndex, string playerId, PlayerAnswers answers, CancellationToken ct = default) => Task.FromResult(true);
        public Task DeleteAsync(string gameId, CancellationToken ct = default) => Task.CompletedTask;

        public Task<T> RunInTransactionAsync<T>(string gameId, Func<Game, (T result, Game? updatedGame)> operation, CancellationToken ct = default)
        {
            if (Game is null) return Task.FromException<T>(new InvalidOperationException("Game not configured in stub"));
            try
            {
                var (result, _) = operation(Game);
                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                return Task.FromException<T>(ex);
            }
        }
    }

    private readonly FakeGameRepo _repo = new();
    private readonly IScoringEngine _scoringEngine = Substitute.For<IScoringEngine>();
    private readonly HostModerationManager _sut;

    private const string GameId = "game-1";
    private const string HostId = "host";
    private const string NonHostId = "other";

    public HostModerationManagerTests()
    {
        _sut = new HostModerationManager(_repo, _scoringEngine);
    }

    private static Game MakeGame(Dictionary<string, int>? roundScores = null)
    {
        var round = new Round
        {
            RoundNumber = 1,
            Letter = 'A',
            Categories = ["Animal"],
            Status = RoundStatus.Locked,
            RoundScores = roundScores ?? new Dictionary<string, int> { ["p1"] = 10, ["p2"] = 5 },
            Answers =
            {
                ["p1"] = new PlayerAnswers
                {
                    PlayerId = "p1",
                    Answers = { ["Animal"] = "Ant" },
                    NormalizedAnswers = { ["Animal"] = "ant" },
                    IsSubmitted = true,
                },
                ["p2"] = new PlayerAnswers
                {
                    PlayerId = "p2",
                    Answers = { ["Animal"] = "Alligator" },
                    NormalizedAnswers = { ["Animal"] = "alligator" },
                    IsSubmitted = true,
                },
            },
        };

        return new Game
        {
            Id = GameId,
            HostPlayerId = HostId,
            Status = GameStatus.RoundResults,
            CurrentRoundIndex = 0,
            Rounds = [round],
            Players =
            [
                new Player { Id = "p1", DisplayName = "Alice", TotalScore = 10 },
                new Player { Id = "p2", DisplayName = "Bob", TotalScore = 5 },
            ],
            Settings = new GameSettings(),
        };
    }

    [Fact]
    public async Task RejectAnswer_ThrowsForNonHost()
    {
        _repo.Game = MakeGame();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.RejectAnswerAsync(GameId, NonHostId, "Animal", "ant"));
    }

    [Fact]
    public async Task RejectAnswer_SetsKeyAndRecalculatesScores()
    {
        var game = MakeGame();
        _repo.Game = game;
        _scoringEngine
            .ComputeRoundScores(Arg.Any<Round>(), Arg.Any<GameSettings>(), Arg.Any<ModerationContext?>())
            .Returns(new Dictionary<string, int> { ["p1"] = 0, ["p2"] = 5 });

        var result = await _sut.RejectAnswerAsync(GameId, HostId, "Animal", "ant");

        Assert.Contains("Animal:ant", game.Rounds[0].RejectedAnswerIds);
        Assert.Equal(0, result.RoundScores["p1"]);
        Assert.Equal(5, result.RoundScores["p2"]);
    }

    [Fact]
    public async Task UnrejectAnswer_RemovesKeyAndRestoresScores()
    {
        var game = MakeGame();
        game.Rounds[0].RejectedAnswerIds.Add("Animal:ant");
        _repo.Game = game;
        _scoringEngine
            .ComputeRoundScores(Arg.Any<Round>(), Arg.Any<GameSettings>(), Arg.Any<ModerationContext?>())
            .Returns(new Dictionary<string, int> { ["p1"] = 10, ["p2"] = 5 });

        var result = await _sut.UnrejectAnswerAsync(GameId, HostId, "Animal", "ant");

        Assert.DoesNotContain("Animal:ant", game.Rounds[0].RejectedAnswerIds);
        Assert.Equal(10, result.RoundScores["p1"]);
    }

    [Fact]
    public async Task MergeAnswers_CreatesMergeGroupAndRecalculatesScores()
    {
        var game = MakeGame();
        _repo.Game = game;
        _scoringEngine
            .ComputeRoundScores(Arg.Any<Round>(), Arg.Any<GameSettings>(), Arg.Any<ModerationContext?>())
            .Returns(new Dictionary<string, int> { ["p1"] = 5, ["p2"] = 5 });

        var (group, result) = await _sut.MergeAnswersAsync(
            GameId, HostId, "Animal", ["ant", "alligator"], "Ant/Alligator");

        Assert.Single(game.Rounds[0].MergeGroups);
        Assert.Equal("Animal", group.Category);
        Assert.Equal("Ant/Alligator", group.CanonicalAnswer);
        Assert.Contains("ant", group.MergedNormalizedAnswers);
        Assert.Contains("alligator", group.MergedNormalizedAnswers);
        Assert.Equal(5, result.RoundScores["p1"]);
        Assert.Equal(5, result.RoundScores["p2"]);
    }

    [Fact]
    public async Task UnmergeAnswers_RemovesGroupAndRestoresScores()
    {
        var game = MakeGame();
        var mergeGroup = new MergeGroup
        {
            Id = "mg-1",
            Category = "Animal",
            CanonicalAnswer = "Ant",
            MergedNormalizedAnswers = ["ant", "alligator"],
        };
        game.Rounds[0].MergeGroups.Add(mergeGroup);
        game.Rounds[0].RoundScores = new Dictionary<string, int> { ["p1"] = 5, ["p2"] = 5 };
        game.Players[0].TotalScore = 5;
        game.Players[1].TotalScore = 5;

        _repo.Game = game;
        _scoringEngine
            .ComputeRoundScores(Arg.Any<Round>(), Arg.Any<GameSettings>(), Arg.Any<ModerationContext?>())
            .Returns(new Dictionary<string, int> { ["p1"] = 10, ["p2"] = 5 });

        var result = await _sut.UnmergeAnswersAsync(GameId, HostId, "mg-1");

        Assert.Empty(game.Rounds[0].MergeGroups);
        Assert.Equal(10, result.RoundScores["p1"]);
    }
}
