using System.Collections.Concurrent;
using Card.Api.Domain;

namespace Card.Api.Services;

public class PlayerConnectionService
{
    private readonly ConcurrentDictionary<string, string> _connections = new();

    public void Bind(string connectionId, string playerName)
    {
        _connections[connectionId] = playerName;
    }

    public string? GetPlayer(string connectionId)
    {
        return _connections.TryGetValue(connectionId, out var name)
            ? name
            : null;
    }

    public void Unbind(string connectionId)
    {
        _connections.TryRemove(connectionId, out _);
    }
}