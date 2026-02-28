using CategoriesBackend.Core.Enums;
using CategoriesBackend.Core.Interfaces;
using CategoriesBackend.Core.Managers;
using CategoriesBackend.Core.Models;
using NSubstitute;

namespace CategoriesBackend.Tests.Unit.Core;

public class GameManagerTests
{
    private readonly IGameRepository _repo = Substitute.For<IGameRepository>();
    private readonly GameManager _sut;

    public GameManagerTests()
    {
        _sut = new GameManager(_repo);
    }

    // --- CreateGameAsync ---

    [Fact]
    public async Task CreateGame_ReturnsGame_WithCorrectHostId()
    {
        var game = await _sut.CreateGameAsync("player-1", "Alice");

        Assert.Equal("player-1", game.HostPlayerId);
    }

    [Fact]
    public async Task CreateGame_ReturnsGame_InLobbyStatus()
    {
        var game = await _sut.CreateGameAsync("player-1", "Alice");

        Assert.Equal(GameStatus.Lobby, game.Status);
    }

    [Fact]
    public async Task CreateGame_AddsHost_ToPlayersList()
    {
        var game = await _sut.CreateGameAsync("player-1", "Alice");

        var host = Assert.Single(game.Players);
        Assert.Equal("player-1", host.Id);
        Assert.Equal("Alice", host.DisplayName);
        Assert.True(host.IsConnected);
    }

    [Fact]
    public async Task CreateGame_AppliesDefaultSettings()
    {
        var game = await _sut.CreateGameAsync("player-1", "Alice");

        Assert.True(game.Settings.IsTimedMode);
        Assert.Equal(60, game.Settings.RoundDurationSeconds);
        Assert.Equal(5, game.Settings.MaxRounds);
        Assert.Equal(10, game.Settings.MaxPlayers);
        Assert.Equal(10, game.Settings.UniqueAnswerPoints);
        Assert.Equal(5, game.Settings.SharedAnswerPoints);
        Assert.Equal(20, game.Settings.BestAnswerBonusPoints);
        Assert.Equal(30, game.Settings.DisputeVotingWindowSeconds);
    }

    [Fact]
    public async Task CreateGame_GeneratesNonEmptyJoinCode()
    {
        var game = await _sut.CreateGameAsync("player-1", "Alice");

        Assert.False(string.IsNullOrWhiteSpace(game.JoinCode));
    }

    [Fact]
    public async Task CreateGame_JoinCode_IsExactlySixCharacters()
    {
        var game = await _sut.CreateGameAsync("player-1", "Alice");

        Assert.Equal(6, game.JoinCode.Length);
    }

    [Fact]
    public async Task CreateGame_JoinCode_ContainsOnlyAllowedCharacters()
    {
        const string allowed = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        var game = await _sut.CreateGameAsync("player-1", "Alice");

        Assert.All(game.JoinCode, ch => Assert.Contains(ch, allowed));
    }

    [Fact]
    public async Task CreateGame_CallsSaveAsync_OnRepository()
    {
        await _sut.CreateGameAsync("player-1", "Alice");

        await _repo.Received(1).SaveAsync(Arg.Any<Game>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateGame_SavesGame_WithNewId()
    {
        Game? saved = null;
        await _repo.SaveAsync(Arg.Do<Game>(g => saved = g), Arg.Any<CancellationToken>());

        await _sut.CreateGameAsync("player-1", "Alice");

        Assert.NotNull(saved);
        Assert.False(string.IsNullOrWhiteSpace(saved.Id));
    }

    [Fact]
    public async Task CreateGame_TwoGames_HaveDifferentIds()
    {
        var game1 = await _sut.CreateGameAsync("player-1", "Alice");
        var game2 = await _sut.CreateGameAsync("player-2", "Bob");

        Assert.NotEqual(game1.Id, game2.Id);
    }

    [Fact]
    public async Task CreateGame_TwoGames_LikelyHaveDifferentJoinCodes()
    {
        // Not guaranteed but statistically certain with 32^6 combinations
        var codes = new HashSet<string>();
        for (int i = 0; i < 10; i++)
        {
            var game = await _sut.CreateGameAsync($"player-{i}", $"Player {i}");
            codes.Add(game.JoinCode);
        }

        Assert.True(codes.Count > 1);
    }

    // --- GetGameAsync ---

    [Fact]
    public async Task GetGame_ThrowsInvalidOperation_WhenNotFound()
    {
        _repo.GetByIdAsync("missing", Arg.Any<CancellationToken>()).Returns((Game?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.GetGameAsync("missing"));
    }

    [Fact]
    public async Task GetGame_ReturnsGame_WhenFound()
    {
        var existing = new Game { Id = "game-1" };
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(existing);

        var result = await _sut.GetGameAsync("game-1");

        Assert.Equal("game-1", result.Id);
    }

    // --- JoinGameAsync ---

    private static Game LobbyGame(int currentPlayers = 1, int maxPlayers = 10) => new()
    {
        Id = "game-1",
        JoinCode = "ABC123",
        HostPlayerId = "host",
        Status = GameStatus.Lobby,
        Players = Enumerable.Range(0, currentPlayers)
            .Select(i => new Player { Id = $"player-{i}", DisplayName = $"Player {i}" })
            .ToList(),
        Settings = new GameSettings { MaxPlayers = maxPlayers }
    };

    [Fact]
    public async Task JoinGame_ThrowsInvalidOperation_WhenJoinCodeNotFound()
    {
        _repo.GetByJoinCodeAsync("NOPE00", Arg.Any<CancellationToken>()).Returns((Game?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.JoinGameAsync("NOPE00", "player-1", "Alice"));
    }

    [Fact]
    public async Task JoinGame_ThrowsInvalidOperation_WhenGameAlreadyStarted()
    {
        var game = LobbyGame();
        game.Status = GameStatus.InRound;
        _repo.GetByJoinCodeAsync("ABC123", Arg.Any<CancellationToken>()).Returns(game);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.JoinGameAsync("ABC123", "player-99", "Late Player"));
    }

    [Fact]
    public async Task JoinGame_ThrowsInvalidOperation_WhenLobbyIsFull()
    {
        var game = LobbyGame(currentPlayers: 10, maxPlayers: 10);
        _repo.GetByJoinCodeAsync("ABC123", Arg.Any<CancellationToken>()).Returns(game);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.JoinGameAsync("ABC123", "player-99", "Late Player"));
    }

    [Fact]
    public async Task JoinGame_AddsPlayer_ToLobby()
    {
        var game = LobbyGame();
        _repo.GetByJoinCodeAsync("ABC123", Arg.Any<CancellationToken>()).Returns(game);

        var result = await _sut.JoinGameAsync("ABC123", "player-new", "New Player");

        Assert.Contains(result.Players, p => p.Id == "player-new" && p.DisplayName == "New Player");
    }

    [Fact]
    public async Task JoinGame_ReturnsGameId_AndCurrentPlayers_AndSettings()
    {
        var game = LobbyGame();
        _repo.GetByJoinCodeAsync("ABC123", Arg.Any<CancellationToken>()).Returns(game);

        var result = await _sut.JoinGameAsync("ABC123", "player-new", "New Player");

        Assert.Equal("game-1", result.Id);
        Assert.NotEmpty(result.Players);
        Assert.NotNull(result.Settings);
    }

    [Fact]
    public async Task JoinGame_CallsSaveAsync_WhenPlayerAdded()
    {
        var game = LobbyGame();
        _repo.GetByJoinCodeAsync("ABC123", Arg.Any<CancellationToken>()).Returns(game);

        await _sut.JoinGameAsync("ABC123", "player-new", "New Player");

        await _repo.Received(1).SaveAsync(Arg.Any<Game>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinGame_IsIdempotent_WhenPlayerAlreadyInLobby()
    {
        var game = LobbyGame();
        _repo.GetByJoinCodeAsync("ABC123", Arg.Any<CancellationToken>()).Returns(game);

        var result = await _sut.JoinGameAsync("ABC123", "player-0", "Player 0");

        Assert.Single(result.Players, p => p.Id == "player-0");
        await _repo.DidNotReceive().SaveAsync(Arg.Any<Game>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinGame_NewPlayer_IsConnected()
    {
        var game = LobbyGame();
        _repo.GetByJoinCodeAsync("ABC123", Arg.Any<CancellationToken>()).Returns(game);

        var result = await _sut.JoinGameAsync("ABC123", "player-new", "New Player");

        var newPlayer = result.Players.Single(p => p.Id == "player-new");
        Assert.True(newPlayer.IsConnected);
    }
}
