using System.Collections.Concurrent;
using CategoriesBackend.Core.Interfaces;

namespace CategoriesBackend.Services;

public class InMemoryPlayerConnectionTracker : IPlayerConnectionTracker
{
    private readonly ConcurrentDictionary<string, (string GameId, string PlayerId)> _connections = new();

    public void Register(string connectionId, string gameId, string playerId)
        => _connections[connectionId] = (gameId, playerId);

    public void Unregister(string connectionId)
        => _connections.TryRemove(connectionId, out _);

    public (string GameId, string PlayerId)? Get(string connectionId)
        => _connections.TryGetValue(connectionId, out var info) ? info : null;
}
