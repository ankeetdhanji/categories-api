namespace CategoriesBackend.Core.Interfaces;

/// <summary>Tracks which SignalR connectionId belongs to which player in which game.</summary>
public interface IPlayerConnectionTracker
{
    void Register(string connectionId, string gameId, string playerId);
    void Unregister(string connectionId);
    (string GameId, string PlayerId)? Get(string connectionId);
}
