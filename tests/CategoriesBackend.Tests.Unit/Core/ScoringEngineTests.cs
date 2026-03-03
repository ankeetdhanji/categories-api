using CategoriesBackend.Core.Managers;
using CategoriesBackend.Core.Models;

namespace CategoriesBackend.Tests.Unit.Core;

public class ScoringEngineTests
{
    private readonly ScoringEngine _sut = new();

    private static GameSettings DefaultSettings() => new();

    /// <summary>Builds a round with players and pre-normalised answers (mirrors SubmitAnswersAsync behaviour).</summary>
    private static Round RoundWith(params (string playerId, Dictionary<string, string> answers)[] playerAnswers)
    {
        var round = new Round
        {
            RoundNumber = 1,
            Letter = 'A',
            Categories = ["Animal", "Country"],
        };

        foreach (var (playerId, answers) in playerAnswers)
        {
            round.Answers[playerId] = new PlayerAnswers
            {
                PlayerId = playerId,
                Answers = new Dictionary<string, string>(answers),
                NormalizedAnswers = answers.ToDictionary(kv => kv.Key, kv => kv.Value.Trim().ToLowerInvariant()),
                IsSubmitted = true,
            };
        }

        return round;
    }

    // --- Unique vs shared ---

    [Fact]
    public void ComputeRoundScores_UniqueAnswers_AwardUniquePoints()
    {
        var round = RoundWith(
            ("p1", new Dictionary<string, string> { ["Animal"] = "Ant" }),
            ("p2", new Dictionary<string, string> { ["Animal"] = "Alligator" }));

        var scores = _sut.ComputeRoundScores(round, DefaultSettings());

        Assert.Equal(10, scores["p1"]);
        Assert.Equal(10, scores["p2"]);
    }

    [Fact]
    public void ComputeRoundScores_SharedAnswers_AwardSharedPoints()
    {
        var round = RoundWith(
            ("p1", new Dictionary<string, string> { ["Animal"] = "Ant" }),
            ("p2", new Dictionary<string, string> { ["Animal"] = "Ant" }));

        var scores = _sut.ComputeRoundScores(round, DefaultSettings());

        Assert.Equal(5, scores["p1"]);
        Assert.Equal(5, scores["p2"]);
    }

    [Fact]
    public void ComputeRoundScores_ThreePlayersShareAnswer_EachAwardSharedPoints()
    {
        var round = RoundWith(
            ("p1", new Dictionary<string, string> { ["Animal"] = "Ant" }),
            ("p2", new Dictionary<string, string> { ["Animal"] = "Ant" }),
            ("p3", new Dictionary<string, string> { ["Animal"] = "Ant" }));

        var scores = _sut.ComputeRoundScores(round, DefaultSettings());

        Assert.Equal(5, scores["p1"]);
        Assert.Equal(5, scores["p2"]);
        Assert.Equal(5, scores["p3"]);
    }

    // --- Empty / missing answers ---

    [Fact]
    public void ComputeRoundScores_WhitespaceAnswer_AwardsZero()
    {
        var round = RoundWith(
            ("p1", new Dictionary<string, string> { ["Animal"] = "   " }),
            ("p2", new Dictionary<string, string> { ["Animal"] = "Ant" }));

        var scores = _sut.ComputeRoundScores(round, DefaultSettings());

        Assert.Equal(0, scores["p1"]);
        Assert.Equal(10, scores["p2"]);
    }

    [Fact]
    public void ComputeRoundScores_MissingCategory_AwardsZero()
    {
        // p1 has no answer for "Animal"
        var round = RoundWith(
            ("p1", new Dictionary<string, string>()),
            ("p2", new Dictionary<string, string> { ["Animal"] = "Ant" }));

        var scores = _sut.ComputeRoundScores(round, DefaultSettings());

        Assert.Equal(0, scores["p1"]);
        Assert.Equal(10, scores["p2"]);
    }

    [Fact]
    public void ComputeRoundScores_AllPlayersEmpty_ReturnsAllZero()
    {
        var round = RoundWith(
            ("p1", new Dictionary<string, string>()),
            ("p2", new Dictionary<string, string>()));

        var scores = _sut.ComputeRoundScores(round, DefaultSettings());

        Assert.Equal(0, scores["p1"]);
        Assert.Equal(0, scores["p2"]);
    }

    // --- Normalisation ---

    [Fact]
    public void ComputeRoundScores_DifferentCasing_TreatedAsShared()
    {
        var round = RoundWith(
            ("p1", new Dictionary<string, string> { ["Animal"] = "ANT" }),
            ("p2", new Dictionary<string, string> { ["Animal"] = "ant" }));

        var scores = _sut.ComputeRoundScores(round, DefaultSettings());

        Assert.Equal(5, scores["p1"]);
        Assert.Equal(5, scores["p2"]);
    }

    [Fact]
    public void ComputeRoundScores_LeadingTrailingWhitespace_TreatedAsShared()
    {
        var round = RoundWith(
            ("p1", new Dictionary<string, string> { ["Animal"] = "  ant  " }),
            ("p2", new Dictionary<string, string> { ["Animal"] = "ant" }));

        var scores = _sut.ComputeRoundScores(round, DefaultSettings());

        Assert.Equal(5, scores["p1"]);
        Assert.Equal(5, scores["p2"]);
    }

    // --- Multiple categories ---

    [Fact]
    public void ComputeRoundScores_MultipleCategories_SumsCorrectly()
    {
        var round = RoundWith(
            ("p1", new Dictionary<string, string> { ["Animal"] = "Ant", ["Country"] = "Austria" }),
            ("p2", new Dictionary<string, string> { ["Animal"] = "Ant", ["Country"] = "Argentina" }));

        var scores = _sut.ComputeRoundScores(round, DefaultSettings());

        // Animal: shared (5+5), Country: unique (10+10)
        Assert.Equal(15, scores["p1"]);
        Assert.Equal(15, scores["p2"]);
    }

    // --- Configurable points ---

    [Fact]
    public void ComputeRoundScores_CustomPoints_AreApplied()
    {
        var round = RoundWith(
            ("p1", new Dictionary<string, string> { ["Animal"] = "Ant" }),      // unique
            ("p2", new Dictionary<string, string> { ["Animal"] = "Alligator" }), // unique
            ("p3", new Dictionary<string, string> { ["Animal"] = "Alligator" })); // shared with p2

        var settings = new GameSettings { UniqueAnswerPoints = 20, SharedAnswerPoints = 8 };
        var scores = _sut.ComputeRoundScores(round, settings);

        Assert.Equal(20, scores["p1"]);
        Assert.Equal(8, scores["p2"]);
        Assert.Equal(8, scores["p3"]);
    }

    // --- Score initialisation ---

    [Fact]
    public void ComputeRoundScores_IncludesEntryForEveryPlayer_EvenWithNoAnswer()
    {
        var round = RoundWith(
            ("p1", new Dictionary<string, string> { ["Animal"] = "Ant" }),
            ("p2", new Dictionary<string, string>()));

        var scores = _sut.ComputeRoundScores(round, DefaultSettings());

        Assert.True(scores.ContainsKey("p1"));
        Assert.True(scores.ContainsKey("p2"));
    }
}
