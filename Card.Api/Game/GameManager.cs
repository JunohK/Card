using System.Collections.Concurrent;

namespace CardGameServer.Game;

public static class GameManager
{
    public static ConcurrentDictionary<string, GameRoom> Rooms = new();
}
