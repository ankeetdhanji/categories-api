using CategoriesBackend.Core.Enums;
using CategoriesBackend.Core.Interfaces;
using CategoriesBackend.Core.Managers;
using CategoriesBackend.Core.Models;
using NSubstitute;

namespace CategoriesBackend.Tests.Unit.Core;

public class DisputeManagerTests
{
    private readonly IGameRepository _repo = Substitute.For<IGameRepository>();
    private readonly DisputeManager _sut;

    public DisputeManagerTests()
    {
        _sut = new DisputeManager(_repo);
    }

    // --- Helpers ---

    private static Game GameWithRound(char letter, params (string playerId, string category, string raw, string norm)[] answers)
    {
        var round = new Round
        {
            RoundNumber = 1,
            Letter = letter,
            Categories = answers.Select(a => a.category).Distinct().ToList(),
            Status = RoundStatus.Locked,
        };

        foreach (var (playerId, category, raw, norm) in answers)
        {
            if (!round.Answers.ContainsKey(playerId))
                round.Answers[playerId] = new PlayerAnswers { PlayerId = playerId };

            round.Answers[playerId].Answers[category] = raw;
            round.Answers[playerId].NormalizedAnswers[category] = norm;
        }

        var players = answers.Select(a => a.playerId).Distinct()
            .Select(id => new Player { Id = id, DisplayName = id })
            .ToList();

        return new Game
        {
            Id = "game-1",
            Status = GameStatus.RoundResults,
            CurrentRoundIndex = 0,
            Rounds = [round],
            Players = players,
            Settings = new GameSettings(),
        };
    }

    private static Game GameWithDisputes(char letter, List<Dispute> disputes, List<Player> players)
    {
        var round = new Round
        {
            RoundNumber = 1,
            Letter = letter,
            Categories = disputes.Select(d => d.Category).Distinct().ToList(),
            Status = RoundStatus.Locked,
            Disputes = disputes,
        };

        return new Game
        {
            Id = "game-1",
            Status = GameStatus.Disputes,
            CurrentRoundIndex = 0,
            Rounds = [round],
            Players = players,
            Settings = new GameSettings(),
        };
    }

    // --- DetectDisputesAsync ---

    [Fact]
    public async Task DetectDisputes_FlagsAnswer_ThatDoesNotStartWithRoundLetter()
    {
        var game = GameWithRound('A',
            ("p1", "Animal", "Bear", "bear"));
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        var disputes = await _sut.DetectDisputesAsync("game-1");

        Assert.Single(disputes);
        Assert.Equal("Animal:bear", disputes[0].Id);
        Assert.Equal("p1", disputes[0].PlayerId);
    }

    [Fact]
    public async Task DetectDisputes_DoesNotFlag_CorrectAnswer()
    {
        var game = GameWithRound('A',
            ("p1", "Animal", "Ant", "ant"));
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        var disputes = await _sut.DetectDisputesAsync("game-1");

        Assert.Empty(disputes);
    }

    [Fact]
    public async Task DetectDisputes_IsCaseInsensitive_OnRoundLetter()
    {
        // Round letter 'A', answer "ant" (lowercase) — should be valid
        var game = GameWithRound('A',
            ("p1", "Animal", "ant", "ant"));
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        var disputes = await _sut.DetectDisputesAsync("game-1");

        Assert.Empty(disputes);
    }

    [Fact]
    public async Task DetectDisputes_SkipsEmptyAnswers()
    {
        var game = GameWithRound('A',
            ("p1", "Animal", "", ""));
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        var disputes = await _sut.DetectDisputesAsync("game-1");

        Assert.Empty(disputes);
    }

    [Fact]
    public async Task DetectDisputes_SetsGameStatus_ToDisputes_WhenAnyFound()
    {
        var game = GameWithRound('A',
            ("p1", "Animal", "Bear", "bear"));
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        await _sut.DetectDisputesAsync("game-1");

        Assert.Equal(GameStatus.Disputes, game.Status);
    }

    [Fact]
    public async Task DetectDisputes_DoesNotChangeStatus_WhenNoDisputesFound()
    {
        var game = GameWithRound('A',
            ("p1", "Animal", "Ant", "ant"));
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        await _sut.DetectDisputesAsync("game-1");

        Assert.Equal(GameStatus.RoundResults, game.Status);
    }

    [Fact]
    public async Task DetectDisputes_ReturnsDisputes_SortedByCategoryThenNormalizedAnswer()
    {
        var game = GameWithRound('A',
            ("p1", "Country", "Brazil", "brazil"),
            ("p2", "Animal", "Dog", "dog"),
            ("p3", "Animal", "Cat", "cat"));
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        var disputes = await _sut.DetectDisputesAsync("game-1");

        Assert.Equal(3, disputes.Count);
        Assert.Equal("Animal", disputes[0].Category);
        Assert.Equal("cat", disputes[0].NormalizedAnswer);
        Assert.Equal("Animal", disputes[1].Category);
        Assert.Equal("dog", disputes[1].NormalizedAnswer);
        Assert.Equal("Country", disputes[2].Category);
    }

    [Fact]
    public async Task DetectDisputes_CallsSaveAsync()
    {
        var game = GameWithRound('A',
            ("p1", "Animal", "Bear", "bear"));
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        await _sut.DetectDisputesAsync("game-1");

        await _repo.Received(1).SaveAsync(Arg.Any<Game>(), Arg.Any<CancellationToken>());
    }

    // --- CastDisputeVoteAsync ---

    private static (Game, Dispute) GameAndDispute(int playerCount = 3)
    {
        var dispute = new Dispute
        {
            Id = "Animal:bear",
            Category = "Animal",
            PlayerId = "p1",
            RawAnswer = "Bear",
            NormalizedAnswer = "bear",
            Status = DisputeStatus.Pending,
        };

        var players = Enumerable.Range(1, playerCount)
            .Select(i => new Player { Id = $"p{i}", DisplayName = $"Player {i}" })
            .ToList();

        var round = new Round
        {
            RoundNumber = 1,
            Letter = 'A',
            Categories = ["Animal"],
            Status = RoundStatus.Locked,
            Disputes = [dispute],
        };

        var game = new Game
        {
            Id = "game-1",
            Status = GameStatus.Disputes,
            CurrentRoundIndex = 0,
            Rounds = [round],
            Players = players,
            Settings = new GameSettings(),
        };

        return (game, dispute);
    }

    [Fact]
    public async Task CastDisputeVote_StoresVote()
    {
        var (game, _) = GameAndDispute();
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        await _sut.CastDisputeVoteAsync("game-1", "p2", "Animal:bear", isValid: true);

        Assert.True(game.Rounds[0].DisputeVotes["Animal:bear"]["p2"]);
    }

    [Fact]
    public async Task CastDisputeVote_Throws_WhenAuthorVotesOnOwnDispute()
    {
        var (game, _) = GameAndDispute();
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CastDisputeVoteAsync("game-1", "p1", "Animal:bear", isValid: true));
    }

    [Fact]
    public async Task CastDisputeVote_OverwritesPreviousVote_FromSamePlayer()
    {
        var (game, _) = GameAndDispute();
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        await _sut.CastDisputeVoteAsync("game-1", "p2", "Animal:bear", isValid: true);
        await _sut.CastDisputeVoteAsync("game-1", "p2", "Animal:bear", isValid: false);

        Assert.False(game.Rounds[0].DisputeVotes["Animal:bear"]["p2"]);
    }

    [Fact]
    public async Task CastDisputeVote_ReturnsVoteCount_AndTotalVoters()
    {
        var (game, _) = GameAndDispute(playerCount: 3); // p1 = author, p2 & p3 can vote
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        var (voteCount, totalVoters, _, _) =
            await _sut.CastDisputeVoteAsync("game-1", "p2", "Animal:bear", isValid: true);

        Assert.Equal(1, voteCount);
        Assert.Equal(2, totalVoters); // 3 players - 1 author
    }

    [Fact]
    public async Task CastDisputeVote_ResolvesValid_WhenMajorityVotesValid()
    {
        // 4 players: p1 = author; p2, p3, p4 vote; majority (2/3) vote valid
        var (game, _) = GameAndDispute(playerCount: 4);
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        await _sut.CastDisputeVoteAsync("game-1", "p2", "Animal:bear", isValid: true);
        await _sut.CastDisputeVoteAsync("game-1", "p3", "Animal:bear", isValid: true);
        var (_, _, resolved, isValid) =
            await _sut.CastDisputeVoteAsync("game-1", "p4", "Animal:bear", isValid: false);

        Assert.True(resolved);
        Assert.True(isValid);
        Assert.Equal(DisputeStatus.Valid, game.Rounds[0].Disputes[0].Status);
    }

    [Fact]
    public async Task CastDisputeVote_ResolvesInvalid_WhenMajorityVotesInvalid()
    {
        var (game, _) = GameAndDispute(playerCount: 4);
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        await _sut.CastDisputeVoteAsync("game-1", "p2", "Animal:bear", isValid: false);
        await _sut.CastDisputeVoteAsync("game-1", "p3", "Animal:bear", isValid: false);
        var (_, _, resolved, isValid) =
            await _sut.CastDisputeVoteAsync("game-1", "p4", "Animal:bear", isValid: true);

        Assert.True(resolved);
        Assert.False(isValid);
        Assert.Equal(DisputeStatus.Invalid, game.Rounds[0].Disputes[0].Status);
    }

    [Fact]
    public async Task CastDisputeVote_TieResolvesAsValid()
    {
        // 3 players: p1 = author; p2 valid, p3 invalid → tie → valid
        var (game, _) = GameAndDispute(playerCount: 3);
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        await _sut.CastDisputeVoteAsync("game-1", "p2", "Animal:bear", isValid: true);
        var (_, _, resolved, isValid) =
            await _sut.CastDisputeVoteAsync("game-1", "p3", "Animal:bear", isValid: false);

        Assert.True(resolved);
        Assert.True(isValid);
        Assert.Equal(DisputeStatus.Valid, game.Rounds[0].Disputes[0].Status);
    }

    [Fact]
    public async Task CastDisputeVote_NotResolved_UntilAllEligiblePlayersVoted()
    {
        // 4 players: p1 = author; need p2, p3, p4 to vote; only p2 votes here
        var (game, _) = GameAndDispute(playerCount: 4);
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        var (_, _, resolved, _) =
            await _sut.CastDisputeVoteAsync("game-1", "p2", "Animal:bear", isValid: true);

        Assert.False(resolved);
    }

    // --- ResolveAllPendingForCategoryAsync ---

    [Fact]
    public async Task ResolveAllPendingForCategory_ResolvesDisputes_ByCurrentVotes()
    {
        var dispute = new Dispute
        {
            Id = "Animal:bear",
            Category = "Animal",
            PlayerId = "p1",
            NormalizedAnswer = "bear",
            Status = DisputeStatus.Pending,
        };

        var players = new List<Player>
        {
            new() { Id = "p1" },
            new() { Id = "p2" },
            new() { Id = "p3" },
        };

        var game = GameWithDisputes('A', [dispute], players);
        game.Rounds[0].DisputeVotes["Animal:bear"] = new Dictionary<string, bool>
        {
            ["p2"] = false,
            ["p3"] = false,
        };
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        await _sut.ResolveAllPendingForCategoryAsync("game-1", "Animal");

        Assert.Equal(DisputeStatus.Invalid, dispute.Status);
    }

    [Fact]
    public async Task ResolveAllPendingForCategory_TreatsNoVotes_AsValid()
    {
        var dispute = new Dispute
        {
            Id = "Animal:bear",
            Category = "Animal",
            PlayerId = "p1",
            NormalizedAnswer = "bear",
            Status = DisputeStatus.Pending,
        };

        var players = new List<Player> { new() { Id = "p1" }, new() { Id = "p2" } };
        var game = GameWithDisputes('A', [dispute], players);
        // No votes cast
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        await _sut.ResolveAllPendingForCategoryAsync("game-1", "Animal");

        Assert.Equal(DisputeStatus.Valid, dispute.Status);
    }

    [Fact]
    public async Task ResolveAllPendingForCategory_DoesNotAffect_AlreadyResolvedDisputes()
    {
        var pending = new Dispute { Id = "Animal:bear", Category = "Animal", PlayerId = "p1", NormalizedAnswer = "bear", Status = DisputeStatus.Pending };
        var alreadyInvalid = new Dispute { Id = "Animal:cat", Category = "Animal", PlayerId = "p2", NormalizedAnswer = "cat", Status = DisputeStatus.Invalid };

        var players = new List<Player> { new() { Id = "p1" }, new() { Id = "p2" }, new() { Id = "p3" } };
        var game = GameWithDisputes('A', [pending, alreadyInvalid], players);
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        await _sut.ResolveAllPendingForCategoryAsync("game-1", "Animal");

        // alreadyInvalid should remain Invalid, not be changed to Valid
        Assert.Equal(DisputeStatus.Invalid, alreadyInvalid.Status);
    }

    [Fact]
    public async Task ResolveAllPendingForCategory_OnlyResolves_DisputesInSpecifiedCategory()
    {
        var animalDispute = new Dispute { Id = "Animal:bear", Category = "Animal", PlayerId = "p1", NormalizedAnswer = "bear", Status = DisputeStatus.Pending };
        var countryDispute = new Dispute { Id = "Country:france", Category = "Country", PlayerId = "p2", NormalizedAnswer = "france", Status = DisputeStatus.Pending };

        var players = new List<Player> { new() { Id = "p1" }, new() { Id = "p2" }, new() { Id = "p3" } };
        var game = GameWithDisputes('A', [animalDispute, countryDispute], players);
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        await _sut.ResolveAllPendingForCategoryAsync("game-1", "Animal");

        // Animal resolved; Country still pending
        Assert.NotEqual(DisputeStatus.Pending, animalDispute.Status);
        Assert.Equal(DisputeStatus.Pending, countryDispute.Status);
    }
}
