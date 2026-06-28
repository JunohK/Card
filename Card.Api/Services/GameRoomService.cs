using Card.Api.Domain;
using System.Collections.Concurrent;

namespace Card.Api.Services;

public partial class GameRoomService
{
    // 🔑 멀티스레드 환경에서 안전한 방 저장소
    private readonly ConcurrentDictionary<string, GameRoom> _rooms = new();

    /// <summary>
    /// 방 생성: 방 객체만 생성하고 저장소에 등록합니다.
    /// </summary>
    public GameRoom CreateRoom(string playerName, string title, string? password, GameMode gameMode = GameMode.Cards)
    {
        var roomId = Guid.NewGuid().ToString().Substring(0, 8);
        var room = new GameRoom
        {
            RoomId = roomId,
            Title = title,
            Password = string.IsNullOrWhiteSpace(password) ? null : password,
            Players = new List<Player>(),
            IsStarted = false,
            CreatedAt = DateTime.UtcNow,
            GameMode = gameMode
        };

        _rooms.TryAdd(roomId, room);
        return room;
    }

    /// <summary>
    /// 방 입장: 중복 입장을 방지하기 위해 기존 유령 세션을 제거한 후 추가합니다.
    /// </summary>
    private int _botCounter = 0;

    /// <summary>방에 봇 1명 추가 (방장만, 게임 시작 전, 최대 7명).</summary>
    public bool AddBot(string roomId)
    {
        if (!_rooms.TryGetValue(roomId, out var room)) return false;
        lock (room)
        {
            if (room.IsStarted) return false;
            if (room.Players.Count >= 7) return false;
            int n = System.Threading.Interlocked.Increment(ref _botCounter);
            var bot = new Player
            {
                PlayerId = $"bot:{n}:{Guid.NewGuid().ToString().Substring(0, 4)}",
                Name = $"🤖 Bot{n}",
                IsBot = true
            };
            room.Players.Add(bot);
        }
        return true;
    }

    public GameRoom? JoinRoom(string roomId, string playerId, string playerName, string? password = null)
    {
        if (!_rooms.TryGetValue(roomId, out var room))
            return null;

        lock (room)
        {
            var nickname = playerName.Trim();

            // 🔴 [수정 핵심] 기존에 '이름'이 같은 플레이어가 있는지 먼저 찾습니다.
            var existingPlayer = room.Players.FirstOrDefault(p => p.Name == nickname);

            // 🔐 비밀번호 검증: 방에 비밀번호가 설정되어 있으면 일치해야 입장 가능
            // existingPlayer(새로고침/재접속) 케이스에도 동일 적용 — 비밀번호 우회 방지
            if (!string.IsNullOrEmpty(room.Password))
            {
                if (password != room.Password)
                {
                    throw new UnauthorizedAccessException("비밀번호가 일치하지 않습니다.");
                }
            }

            if (existingPlayer != null)
            {
                // 1. 새로고침한 유저라면 기존 객체의 PlayerId(ConnectionId)만 새 것으로 바꿉니다.
                // 이렇게 하면 리스트 순서가 유지되어 내 위치가 아래로 밀리지 않습니다.
                string oldId = existingPlayer.PlayerId;
                existingPlayer.PlayerId = playerId;

                // 2. 만약 이 사람이 방장이었다면, 방장 ID도 새 ID로 즉시 갱신합니다.
                if (room.HostPlayerId == oldId)
                {
                    room.HostPlayerId = playerId;
                }

                // 3. 게임 진행 중 재접속 — Bang/Omok/Cards 하위 상태의 옛 ConnectionId를
                //    새 ConnectionId로 전파해야 턴/대상/응답이 깨지지 않음.
                RemapPlayerId(room, oldId, playerId);
            }
            else
            {
                // 완전히 처음 들어오는 유저인 경우에만 새로 추가합니다.
                if (room.Players.Count >= 7)
                    throw new Exception("방 인원이 가득 찼습니다.");

                var newPlayer = new Player
                {
                    PlayerId = playerId,
                    Name = nickname,
                    Hand = new List<PlayingCard>()
                };
                room.Players.Add(newPlayer);

                // 첫 번째 입장객에게 방장 권한 부여
                if (string.IsNullOrEmpty(room.HostPlayerId))
                {
                    room.HostPlayerId = playerId;
                }
            }
        }

        return room;
    }

    /// <summary>
    /// 방 나가기: 인원이 0명이 되면 방을 완전히 삭제합니다.
    /// </summary>
    public bool LeaveRoom(string roomId, string playerId)
    {
        if (!_rooms.TryGetValue(roomId, out var room)) return false;

        lock (room)
        {
            room.Players.RemoveAll(p => p.PlayerId == playerId);

            if (room.Players.Count == 0)
            {
                _rooms.TryRemove(roomId, out _);
                return true; 
            }

            if (room.HostPlayerId == playerId && room.Players.Count > 0)
            {
                room.HostPlayerId = room.Players[0].PlayerId;
            }
        }

        return false;
    }

    public GameRoom? GetRoom(string roomId)
    {
        _rooms.TryGetValue(roomId, out var room);
        return room;
    }

    public IEnumerable<GameRoom> GetRooms()
    {
        return _rooms.Values;
    }

    /// <summary>재접속으로 ConnectionId가 바뀌면 oldId를 newId로 모든 게임 하위 상태에 전파.
    /// (Cards 방 상태 + Bang + Omok). 턴/대상/응답 큐가 옛 ID를 가리켜 깨지는 것을 방지.</summary>
    private static void RemapPlayerId(GameRoom room, string oldId, string newId)
    {
        if (string.IsNullOrEmpty(oldId) || oldId == newId) return;

        // Cards / 공통 방 상태
        if (room.CurrentTurnPlayerId == oldId) room.CurrentTurnPlayerId = newId;
        if (room.HostPlayerId == oldId) room.HostPlayerId = newId;
        if (room.WinnerPlayerId == oldId) room.WinnerPlayerId = newId;
        if (room.LastActorPlayerId == oldId) room.LastActorPlayerId = newId;
        if (room.StopCallerId == oldId) room.StopCallerId = newId;

        // 오목
        if (room.Omok != null)
        {
            var o = room.Omok;
            if (o.BlackPlayerId == oldId) o.BlackPlayerId = newId;
            if (o.WhitePlayerId == oldId) o.WhitePlayerId = newId;
            if (o.WinnerPlayerId == oldId) o.WinnerPlayerId = newId;
        }

        // 뱅
        if (room.Bang != null)
        {
            var b = room.Bang;
            if (b.CurrentTurnPlayerId == oldId) b.CurrentTurnPlayerId = newId;
            foreach (var bp in b.Players)
                if (bp.PlayerId == oldId) bp.PlayerId = newId;
            if (b.Pending != null)
            {
                var pd = b.Pending;
                if (pd.TargetPlayerId == oldId) pd.TargetPlayerId = newId;
                if (pd.SourcePlayerId == oldId) pd.SourcePlayerId = newId;
                for (int i = 0; i < pd.ResponderQueue.Count; i++)
                    if (pd.ResponderQueue[i] == oldId) pd.ResponderQueue[i] = newId;
            }
            for (int i = 0; i < b.WinnerPlayerIds.Count; i++)
                if (b.WinnerPlayerIds[i] == oldId) b.WinnerPlayerIds[i] = newId;
        }
    }

}
