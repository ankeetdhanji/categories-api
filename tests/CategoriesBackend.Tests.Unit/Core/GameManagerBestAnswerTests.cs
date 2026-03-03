using CategoriesBackend.Core.Enums;
using CategoriesBackend.Core.Interfaces;
using CategoriesBackend.Core.Managers;
using CategoriesBackend.Core.Models;
using NSubstitute;

namespace CategoriesBackend.Tests.Unit.Core;

/// <summary>
/// Unit tests for GameManager.ApplyBestAnswerBonusAsync:
/// best-answer vote tallying, bonus award, tie-split, and host validation.
/// </summary>
public class GameManagerBestAnswerTests
{
    private readonly IGameRepository _repo = Substitute.For<IGameRepository>();
    private readonly GameManager _sut;

    public GameManagerBestAnswerTests()
    {
        _sut = new GameManager(_repo);
    }

    // --- Helpers ---

    private static Game GameWithRounds(List<Player> players, List<Round> rounds, int bonusPoints = 20)
    {
        return new Game
        {
            Id = "game-1",
            HostPlayerId = players[0].Id,
            Status = GameStatus.RoundResults,
            Players = players,
            Rounds = rounds,
            Settings = new GameSettings { BestAnswerBonusPoints = bonusPoints },
        };
    }

    private static Round RoundWithLikes(char letter,
        Dictionary<string, PlayerAnswers> answers,
        Dictionary<string, Dictionary<string, string>> categoryLikes)
    {
        return new Round
        {
            RoundNumber = 1,
            Letter = letter,
            Categories = answers.Values.SelectMany(pa => pa.NormalizedAnswers.Keys).Distinct().ToList(),
            Status = RoundStatus.Locked,
            Answers = answers,
            CategoryLikes = categoryLikes,
        };
    }

    private static PlayerAnswers PA(string playerId, params (string category, string norm)[] answers) =>
        new()
        {
            PlayerId = playerId,
            Answers = answers.ToDictionary(a => a.category, a => a.norm),
            NormalizedAnswers = answers.ToDictionary(a => a.category, a => a.norm),
            IsSubmitted = true,
        };

    // --- ApplyBestAnswerBonusAsync ---

    [Fact]
    public async Task ApplyBonus_ThrowsUnauthorized_WhenNonHostRequests()
    {
        var players = new List<Player>
        {
            new() { Id = "host", DisplayName = "Host" },
            new() { Id = "p2", DisplayName = "P2" },
        };
        var game = GameWithRounds(players, []);
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.ApplyBestAnswerBonusAsync("game-1", "p2"));
    }

    [Fact]
    public async Task ApplyBonus_AwardsBonusToSoleWinner()
    {
        var players = new List<Player>
        {
            new() { Id = "host", DisplayName = "Host", TotalScore = 10 },
            new() { Id = "p2",   DisplayName = "P2",   TotalScore = 5  },
        };

        // p2 answered "ant" in Animal; host liked p2's answer
        var round = RoundWithLikes('A',
            answers: new()
            {
                ["host"] = PA("host", ("Animal", "alligator")),
                ["p2"]   = PA("p2",   ("Animal", "ant")),
            },
            categoryLikes: new()
            {
                ["Animal"] = new() { ["host"] = "ant" }, // host liked p2's "ant"
            });

        var game = GameWithRounds(players, [round], bonusPoints: 20);
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        var result = await _sut.ApplyBestAnswerBonusAsync("game-1", "host");

        Assert.Equal(["p2"], result.WinnerPlayerIds);
        Assert.Equal(20, result.BonusPerWinner);
        Assert.Equal(25, players.Single(p => p.Id == "p2").TotalScore); // 5 + 20
        Assert.Equal(10, players.Single(p => p.Id == "host").TotalScore); // unchanged
    }

    [Fact]
    public async Task ApplyBonus_SplitsBonusEvenly_OnTie()
    {
        var players = new List<Player>
        {
            new() { Id = "host", DisplayName = "Host", TotalScore = 10 },
            new() { Id = "p2",   DisplayName = "P2",   TotalScore = 8  },
            new() { Id = "p3",   DisplayName = "P3",   TotalScore = 6  },
        };

        // p2 and p3 each get 1 like → tie; bonus 20 / 2 = 10 each
        var round = RoundWithLikes('A',
            answers: new()
            {
                ["host"] = PA("host", ("Animal", "alligator")),
                ["p2"]   = PA("p2",   ("Animal", "ant")),
                ["p3"]   = PA("p3",   ("Animal", "armadillo")),
            },
            categoryLikes: new()
            {
                ["Animal"] = new()
                {
                    ["p2"] = "alligator", // p2 liked host's answer
                    ["p3"] = "ant",       // p3 liked p2's answer → p2 gets a vote
                    // Wait: p2 liked host, p3 liked p2; let me recalculate:
                    // host gets 1 vote (from p2), p2 gets 1 vote (from p3) → tied!
                },
            });

        // Actually: host voted for "alligator" (host's own — but wait, self-vote is UI-enforced,
        // not server-enforced via like. The test just needs tied vote counts.
        // Let's redo: p3 liked "ant" (p2's answer), p2 liked "armadillo" (p3's answer) → tie p2/p3.
        round.CategoryLikes["Animal"] = new()
        {
            ["p2"] = "armadillo", // p2 liked p3's answer → p3 gets vote
            ["p3"] = "ant",       // p3 liked p2's answer → p2 gets vote
        };

        var game = GameWithRounds(players, [round], bonusPoints: 20);
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        var result = await _sut.ApplyBestAnswerBonusAsync("game-1", "host");

        Assert.Equal(2, result.WinnerPlayerIds.Count);
        Assert.Contains("p2", result.WinnerPlayerIds);
        Assert.Contains("p3", result.WinnerPlayerIds);
        Assert.Equal(10, result.BonusPerWinner); // 20 / 2

        Assert.Equal(18, players.Single(p => p.Id == "p2").TotalScore); // 8 + 10
        Assert.Equal(16, players.Single(p => p.Id == "p3").TotalScore); // 6 + 10
        Assert.Equal(10, players.Single(p => p.Id == "host").TotalScore); // unchanged
    }

    [Fact]
    public async Task ApplyBonus_AwardsNoBonus_WhenNoLikesWereCast()
    {
        var players = new List<Player>
        {
            new() { Id = "host", DisplayName = "Host", TotalScore = 10 },
            new() { Id = "p2",   DisplayName = "P2",   TotalScore = 5  },
        };

        var round = RoundWithLikes('A',
            answers: new()
            {
                ["host"] = PA("host", ("Animal", "alligator")),
                ["p2"]   = PA("p2",   ("Animal", "ant")),
            },
            categoryLikes: []); // no likes at all

        var game = GameWithRounds(players, [round], bonusPoints: 20);
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        var result = await _sut.ApplyBestAnswerBonusAsync("game-1", "host");

        Assert.Empty(result.WinnerPlayerIds);
        Assert.Equal(0, result.BonusPerWinner);
        Assert.Equal(10, players.Single(p => p.Id == "host").TotalScore);
        Assert.Equal(5, players.Single(p => p.Id == "p2").TotalScore);
    }

    [Fact]
    public async Task ApplyBonus_TalliesVotes_AcrossMultipleRoundsAndCategories()
    {
        var players = new List<Player>
        {
            new() { Id = "host", DisplayName = "Host", TotalScore = 0 },
            new() { Id = "p2",   DisplayName = "P2",   TotalScore = 0 },
        };

        // Round 1: host gets a like in Animal
        var round1 = RoundWithLikes('A',
            answers: new()
            {
                ["host"] = PA("host", ("Animal", "alligator"), ("Country", "austria")),
                ["p2"]   = PA("p2",   ("Animal", "ant"),       ("Country", "albania")),
            },
            categoryLikes: new()
            {
                ["Animal"]  = new() { ["p2"] = "alligator" }, // p2 liked host → host +1
                ["Country"] = new() { ["p2"] = "austria" },   // p2 liked host again → host +1
            });

        // Round 2: p2 gets a like in Animal
        var round2 = new Round
        {
            RoundNumber = 2,
            Letter = 'B',
            Categories = ["Animal"],
            Status = RoundStatus.Locked,
            Answers = new()
            {
                ["host"] = PA("host", ("Animal", "bear")),
                ["p2"]   = PA("p2",   ("Animal", "bat")),
            },
            CategoryLikes = new()
            {
                ["Animal"] = new() { ["host"] = "bat" }, // host liked p2 → p2 +1
            },
        };

        var game = GameWithRounds(players, [round1, round2], bonusPoints: 20);
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        var result = await _sut.ApplyBestAnswerBonusAsync("game-1", "host");

        // host: 2 votes, p2: 1 vote → host wins
        Assert.Equal(2, result.VotesByPlayer["host"]);
        Assert.Equal(1, result.VotesByPlayer["p2"]);
        Assert.Equal(["host"], result.WinnerPlayerIds);
        Assert.Equal(20, result.BonusPerWinner);
    }

    [Fact]
    public async Task ApplyBonus_UpdatesPlayerBestAnswerVotes()
    {
        var players = new List<Player>
        {
            new() { Id = "host", DisplayName = "Host" },
            new() { Id = "p2",   DisplayName = "P2" },
        };

        var round = RoundWithLikes('A',
            answers: new()
            {
                ["host"] = PA("host", ("Animal", "alligator")),
                ["p2"]   = PA("p2",   ("Animal", "ant")),
            },
            categoryLikes: new()
            {
                ["Animal"] = new() { ["host"] = "ant" }, // host liked p2 → p2 +1
            });

        var game = GameWithRounds(players, [round]);
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        await _sut.ApplyBestAnswerBonusAsync("game-1", "host");

        Assert.Equal(1, players.Single(p => p.Id == "p2").BestAnswerVotes);
        Assert.Equal(0, players.Single(p => p.Id == "host").BestAnswerVotes);
    }

    [Fact]
    public async Task ApplyBonus_SetsGameStatus_ToFinished()
    {
        var players = new List<Player> { new() { Id = "host", DisplayName = "Host" } };
        var game = GameWithRounds(players, []);
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        await _sut.ApplyBestAnswerBonusAsync("game-1", "host");

        Assert.Equal(GameStatus.Finished, game.Status);
    }

    [Fact]
    public async Task ApplyBonus_CallsSaveAsync()
    {
        var players = new List<Player> { new() { Id = "host", DisplayName = "Host" } };
        var game = GameWithRounds(players, []);
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        await _sut.ApplyBestAnswerBonusAsync("game-1", "host");

        await _repo.Received(1).SaveAsync(Arg.Any<Game>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyBonus_ReturnsFinalLeaderboard_SortedByTotalScoreDescending()
    {
        var players = new List<Player>
        {
            new() { Id = "host", DisplayName = "Host", TotalScore = 5 },
            new() { Id = "p2",   DisplayName = "P2",   TotalScore = 15 },
        };

        var round = RoundWithLikes('A',
            answers: new()
            {
                ["host"] = PA("host", ("Animal", "alligator")),
                ["p2"]   = PA("p2",   ("Animal", "ant")),
            },
            categoryLikes: []);

        var game = GameWithRounds(players, [round]);
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        var result = await _sut.ApplyBestAnswerBonusAsync("game-1", "host");

        Assert.Equal("p2", result.FinalLeaderboard[0].PlayerId);
        Assert.Equal("host", result.FinalLeaderboard[1].PlayerId);
    }

    [Fact]
    public async Task ApplyBonus_OddBonusPoints_FloorDivision_OnTie()
    {
        // 3-way tie with 21 bonus points → 21 / 3 = 7 each (remainder dropped)
        var players = new List<Player>
        {
            new() { Id = "host", DisplayName = "Host", TotalScore = 10 },
            new() { Id = "p2",   DisplayName = "P2",   TotalScore = 10 },
            new() { Id = "p3",   DisplayName = "P3",   TotalScore = 10 },
        };

        var round = RoundWithLikes('A',
            answers: new()
            {
                ["host"] = PA("host", ("Animal", "alligator")),
                ["p2"]   = PA("p2",   ("Animal", "ant")),
                ["p3"]   = PA("p3",   ("Animal", "armadillo")),
            },
            categoryLikes: new()
            {
                ["Animal"] = new()
                {
                    ["p2"]   = "alligator", // p2 liked host → host +1
                    ["p3"]   = "ant",       // p3 liked p2  → p2  +1
                    ["host"] = "armadillo", // host liked p3 → p3  +1
                },
            });

        var game = GameWithRounds(players, [round], bonusPoints: 21);
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        var result = await _sut.ApplyBestAnswerBonusAsync("game-1", "host");

        Assert.Equal(3, result.WinnerPlayerIds.Count);
        Assert.Equal(7, result.BonusPerWinner);
        Assert.All(players, p => Assert.Equal(17, p.TotalScore)); // 10 + 7
    }
}
