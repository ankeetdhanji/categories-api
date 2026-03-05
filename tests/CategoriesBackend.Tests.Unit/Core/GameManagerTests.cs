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
    public async Task JoinGame_ThrowsInvalidOperation_WhenGameIsFinished()
    {
        var game = LobbyGame();
        game.Status = GameStatus.Finished;
        _repo.GetByJoinCodeAsync("ABC123", Arg.Any<CancellationToken>()).Returns(game);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.JoinGameAsync("ABC123", "player-99", "Late Player"));
    }

    [Fact]
    public async Task JoinGame_WhenGameInRound_AddsPlayerAsSpectating()
    {
        var game = LobbyGame();
        game.Status = GameStatus.InRound;
        _repo.GetByJoinCodeAsync("ABC123", Arg.Any<CancellationToken>()).Returns(game);

        var result = await _sut.JoinGameAsync("ABC123", "player-99", "Late Player");

        var newPlayer = result.Players.Single(p => p.Id == "player-99");
        Assert.True(newPlayer.IsSpectating);
    }

    [Fact]
    public async Task JoinGame_WhenGameInLobby_AddsPlayerNotSpectating()
    {
        var game = LobbyGame();
        _repo.GetByJoinCodeAsync("ABC123", Arg.Any<CancellationToken>()).Returns(game);

        var result = await _sut.JoinGameAsync("ABC123", "player-new", "New Player");

        var newPlayer = result.Players.Single(p => p.Id == "player-new");
        Assert.False(newPlayer.IsSpectating);
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

    // --- BeginRoundAsync spectating clear ---

    [Fact]
    public async Task BeginRound_ClearsIsSpectating_ForAllPlayers()
    {
        var game = new Game
        {
            Id = "game-1",
            Status = GameStatus.Starting,
            Players =
            [
                new Player { Id = "p1", IsConnected = true, IsSpectating = false },
                new Player { Id = "p2", IsConnected = true, IsSpectating = true },
            ],
            Rounds = [new Round { RoundNumber = 1, Letter = 'A', Categories = ["Animal"] }],
            Settings = new GameSettings { IsTimedMode = false },
        };
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        await _sut.BeginRoundAsync("game-1");

        Assert.All(game.Players, p => Assert.False(p.IsSpectating));
    }

    // --- SetPlayerConnectedAsync ---

    private static Game ActiveGame() => new()
    {
        Id = "game-1",
        HostPlayerId = "host",
        Status = GameStatus.InRound,
        Players =
        [
            new Player { Id = "host", DisplayName = "Host", IsConnected = true },
            new Player { Id = "p2",   DisplayName = "Bob",  IsConnected = true },
        ],
        Settings = new GameSettings(),
    };

    [Fact]
    public async Task SetPlayerConnected_MarksPlayer_Disconnected()
    {
        var game = ActiveGame();
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        await _sut.SetPlayerConnectedAsync("game-1", "host", false);

        Assert.False(game.Players.Single(p => p.Id == "host").IsConnected);
    }

    [Fact]
    public async Task SetPlayerConnected_MarksPlayer_Connected()
    {
        var game = ActiveGame();
        game.Players[0].IsConnected = false;
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        await _sut.SetPlayerConnectedAsync("game-1", "host", true);

        Assert.True(game.Players.Single(p => p.Id == "host").IsConnected);
    }

    [Fact]
    public async Task SetPlayerConnected_IsNoOp_WhenGameNotFound()
    {
        _repo.GetByIdAsync("missing", Arg.Any<CancellationToken>()).Returns((Game?)null);

        // Should not throw
        await _sut.SetPlayerConnectedAsync("missing", "host", false);

        await _repo.DidNotReceive().SaveAsync(Arg.Any<Game>(), Arg.Any<CancellationToken>());
    }

    // --- TransferHostAsync ---

    [Fact]
    public async Task TransferHost_AssignsNewHost_ToFirstConnectedNonHostPlayer()
    {
        var game = ActiveGame();
        game.Players[0].IsConnected = false; // host disconnected
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        var newHostId = await _sut.TransferHostAsync("game-1", "host");

        Assert.Equal("p2", newHostId);
        Assert.Equal("p2", game.HostPlayerId);
    }

    [Fact]
    public async Task TransferHost_ReturnsNull_WhenHostHasReconnected()
    {
        var game = ActiveGame(); // host IsConnected = true
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        var newHostId = await _sut.TransferHostAsync("game-1", "host");

        Assert.Null(newHostId);
        Assert.Equal("host", game.HostPlayerId); // unchanged
    }

    [Fact]
    public async Task TransferHost_ReturnsNull_WhenHostAlreadyChanged()
    {
        var game = ActiveGame();
        game.HostPlayerId = "p2"; // already transferred
        game.Players[0].IsConnected = false;
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        var newHostId = await _sut.TransferHostAsync("game-1", "host");

        Assert.Null(newHostId);
    }

    [Fact]
    public async Task TransferHost_ReturnsNull_WhenNoConnectedPlayersExist()
    {
        var game = ActiveGame();
        game.Players[0].IsConnected = false;
        game.Players[1].IsConnected = false;
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        var newHostId = await _sut.TransferHostAsync("game-1", "host");

        Assert.Null(newHostId);
    }

    [Fact]
    public async Task TransferHost_CallsSaveAsync_WhenTransferSucceeds()
    {
        var game = ActiveGame();
        game.Players[0].IsConnected = false;
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        await _sut.TransferHostAsync("game-1", "host");

        await _repo.Received(1).SaveAsync(Arg.Any<Game>(), Arg.Any<CancellationToken>());
    }

    // --- BeginNextRoundAsync ---

    private static Game TwoRoundGame(int currentRoundIndex = 0) => new()
    {
        Id = "game-1",
        Status = GameStatus.RoundResults,
        CurrentRoundIndex = currentRoundIndex,
        Players = [new Player { Id = "p1", IsConnected = true }],
        Rounds =
        [
            new Round { RoundNumber = 1, Letter = 'A', Categories = ["Animal"], Status = RoundStatus.Complete },
            new Round { RoundNumber = 2, Letter = 'B', Categories = ["Animal"] },
        ],
        Settings = new GameSettings { IsTimedMode = false, RoundDurationSeconds = 60 },
    };

    [Fact]
    public async Task BeginNextRound_AdvancesCurrentRoundIndex()
    {
        var game = TwoRoundGame(currentRoundIndex: 0);
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        await _sut.BeginNextRoundAsync("game-1");

        Assert.Equal(1, game.CurrentRoundIndex);
    }

    [Fact]
    public async Task BeginNextRound_SetsGameStatus_ToInRound()
    {
        var game = TwoRoundGame(currentRoundIndex: 0);
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        await _sut.BeginNextRoundAsync("game-1");

        Assert.Equal(GameStatus.InRound, game.Status);
    }

    [Fact]
    public async Task BeginNextRound_SetsRoundStatus_ToAnswering()
    {
        var game = TwoRoundGame(currentRoundIndex: 0);
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        await _sut.BeginNextRoundAsync("game-1");

        Assert.Equal(RoundStatus.Answering, game.Rounds[1].Status);
    }

    [Fact]
    public async Task BeginNextRound_ReturnsNull_WhenAllRoundsPlayed()
    {
        var game = TwoRoundGame(currentRoundIndex: 1); // already on last round
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        var result = await _sut.BeginNextRoundAsync("game-1");

        Assert.Null(result);
    }

    [Fact]
    public async Task BeginNextRound_ClearsIsSpectating_ForAllPlayers()
    {
        var game = TwoRoundGame(currentRoundIndex: 0);
        game.Players[0].IsSpectating = true;
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        await _sut.BeginNextRoundAsync("game-1");

        Assert.All(game.Players, p => Assert.False(p.IsSpectating));
    }

    [Fact]
    public async Task BeginNextRound_SetsEndedAt_InTimedMode()
    {
        var game = TwoRoundGame(currentRoundIndex: 0);
        game.Settings.IsTimedMode = true;
        game.Settings.RoundDurationSeconds = 60;
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        await _sut.BeginNextRoundAsync("game-1");

        Assert.NotNull(game.Rounds[1].EndedAt);
    }

    [Fact]
    public async Task BeginNextRound_DoesNotSetEndedAt_InRelaxedMode()
    {
        var game = TwoRoundGame(currentRoundIndex: 0);
        game.Settings.IsTimedMode = false;
        _repo.GetByIdAsync("game-1", Arg.Any<CancellationToken>()).Returns(game);

        await _sut.BeginNextRoundAsync("game-1");

        Assert.Null(game.Rounds[1].EndedAt);
    }
}
