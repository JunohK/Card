using Card.Api.Domain;
using System.Collections.Concurrent;

namespace Card.Api.Services;

public class GameRoomService
{
    // 멀티스레드의 안전한 메모리 저장소
    private readonly ConcurrentDictionary<string, GameRoom> _rooms = new();

    /// <summary>
    /// 게임 방 생성
    /// </summary>
    
    public GameRoom CreateRoom(string hostPlayerName)
    {
        var room = new GameRoom();
        var host = new Player
        {
            Name = hostPlayerName
        };

        room.Players.Add(host);
        room.State.CurrentTurnPlayerId = host.PlayerId;

        _rooms[room.RoomId] = room;
        return room;
    }

    /// <summary>
    /// 게임 방 입장
    /// </summary>
    public GameRoom? JoinRoom(string roomId, string playerName)
    {
        if(!_rooms.TryGetValue(roomId, out var room))
            return null;

        // 중복이름 방지
        if(room.Players.Any(p => p.Name == playerName))
            return null;

        room.Players.Add(new Player
        {
            Name = playerName
        });

        return room;
    }

    /// <summary>
    /// 게임 상태 조회
    /// </summary>
    public GameRoom? GetRoom(string roomId)
    {
        _rooms.TryGetValue(roomId, out var room);
        {
            return room;
        }
    }
}