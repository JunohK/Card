using Card.Api.Domain;
using System.Collections.Concurrent;

namespace Card.Api.Services;

public partial class GameRoomService
{
    // 🔑 멀티스레드 환경에서 안전한 방 저장소
    private readonly ConcurrentDictionary<string, GameRoom> _rooms = new();

    // 🔒 방 단위 직렬화 락. 한 방의 상태 변경 액션이 동시에 실행되지 않도록 보장한다.
    // (lock(room)과 달리 await 구간을 넘어 유지 가능 → SignalR 핸들러의 비동기 처리에 필요)
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _roomLocks = new();

    /// <summary>방 락을 획득하고, Dispose 시 해제하는 releaser를 반환한다.
    /// 사용법: <c>using (await _roomService.LockRoomAsync(roomId)) { ... }</c></summary>
    public async Task<IDisposable> LockRoomAsync(string roomId)
    {
        var gate = _roomLocks.GetOrAdd(roomId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        return new Releaser(gate);
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _gate;
        public Releaser(SemaphoreSlim gate) => _gate = gate;
        public void Dispose()
        {
            // 락을 잡은 쪽이 참조를 들고 있으므로, 방 삭제로 레지스트리에서 빠져도 안전하게 해제된다.
            _gate?.Release();
            _gate = null;
        }
    }

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
                // 진행 중인 게임에는 새 플레이어가 끼어들 수 없음 (오목은 관전 허용)
                if (room.IsStarted && room.GameMode != GameMode.Omok)
                    throw new InvalidOperationException("게임이 진행 중인 방에는 입장할 수 없습니다.");
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
            int leavingIndex = room.Players.FindIndex(p => p.PlayerId == playerId);
            var leaving = leavingIndex >= 0 ? room.Players[leavingIndex] : null;
            room.Players.RemoveAll(p => p.PlayerId == playerId);

            if (room.Players.Count == 0)
            {
                _rooms.TryRemove(roomId, out _);
                // 락 레지스트리에서도 제거(Dispose는 하지 않음 — 현재 락을 잡은 releaser가 안전히 해제하도록).
                _roomLocks.TryRemove(roomId, out _);
                return true;
            }

            if (room.HostPlayerId == playerId && room.Players.Count > 0)
            {
                room.HostPlayerId = room.Players[0].PlayerId;
            }

            // 진행 중인 Cards 게임에서 나간 경우 — 턴이 멈추거나 혼자 남은 채 계속되지 않도록 정리
            if (leaving != null && room.GameMode == GameMode.Cards && room.IsStarted && !room.IsFinished)
            {
                if (room.Players.Count < 2)
                {
                    // 상대가 없으면 게임 종료. 남은 사람 중 총점 최저가 승자
                    room.IsFinished = true;
                    room.IsStarted = false;
                    room.WinnerName = $"{leaving.Name} (퇴장)";
                    SetGameWinners(room);
                }
                else if (room.CurrentTurnPlayerId == playerId)
                {
                    // 나간 사람의 턴이었으면 원래 순서상 다음 자리로 턴 이동 (허브 MoveToNextTurn과 동일 규칙)
                    var next = room.Players[leavingIndex % room.Players.Count];
                    room.CurrentTurnPlayerId = next.PlayerId;
                    room.TurnStartedAt = DateTime.UtcNow;
                    next.RoundTurnCount++;
                }
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
