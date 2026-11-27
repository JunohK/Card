using Card.Api.Services;
using Card.Api.Domain;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Card.Hubs;

// 로비·방 입장/퇴장·방 설정·채팅·프로필
public partial class GameHub
{
    // ✅ 로비 진입 (프로필 객체 전송 및 목록 갱신)
    public async Task EnterLobby()
    {
        var nickname = Context.User?.Identity?.Name ?? "Unknown";

        // 로비 진입 = 들어 있던 모든 방에서 퇴장 (기권 후 로비 이동·통계 버튼·뒤로가기 등 모든 경로 공통)
        foreach (var r in _roomService.GetRoomsOf(PlayerKey))
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, r.RoomId);
        await LeaveAllRoomsAsync(PlayerKey, Clients, _context, _roomService, _omokService);

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Nickname == nickname);

        object profile = user != null
            ? new
            {
                name = user.Nickname,
                wins = user.Wins,
                totalGames = user.TotalGames,
                maxScore = user.MaxScore,
                minScore = user.MinScore
            }
            : new { name = nickname, wins = 0, totalGames = 0, maxScore = 0, minScore = 0 };

        await Clients.Caller.SendAsync("ConnectedUser", profile);
        await SendRoomListToAll();
    }

    // ✅ 방 생성
    // gameMode: 0=Cards, 1=Omok (기본 Cards로 기존 클라이언트 호환)
    public async Task CreateRoom(string title, string? password, int gameMode = 0)
    {
        var nickname = Context.User?.Identity?.Name ?? "Unknown";
        var mode = Enum.IsDefined(typeof(GameMode), gameMode) ? (GameMode)gameMode : GameMode.Cards;
        var room = _roomService.CreateRoom(nickname, title, password, mode);

        // 방 생성 직후 입장 처리
        _roomService.JoinRoom(room.RoomId, PlayerKey, nickname, password);

        await Groups.AddToGroupAsync(Context.ConnectionId, room.RoomId);
        await Clients.Caller.SendAsync("RoomCreated", room.RoomId);
        await SendRoomListToAll();
    }

    // ✅ [문제의 부분 수정] 방 입장
    public async Task JoinRoom(string roomId, string? password)
    {
        // 1. 방 정보 확인
        var room = _roomService.GetRoom(roomId);
        if (room == null) throw new HubException("방을 찾을 수 없습니다.");

        // 2. 닉네임 추출
        var nickname = Context.User?.Identity?.Name ?? "Unknown";

        using (await _roomService.LockRoomAsync(roomId))
        {
            // 3. 서비스 호출 — 비밀번호/인원 등 검증 실패 시 예외 발생
            GameRoom? updatedRoom;
            try
            {
                updatedRoom = _roomService.JoinRoom(roomId, PlayerKey, nickname, password);
            }
            catch (UnauthorizedAccessException ex)
            {
                // 비밀번호 불일치 — 클라이언트에 명확한 메시지 + invoke 실패로 알림
                await Clients.Caller.SendAsync("ErrorMessage", ex.Message);
                throw new HubException(ex.Message);
            }
            catch (Exception ex)
            {
                // 인원 초과 등 기타 예외
                await Clients.Caller.SendAsync("ErrorMessage", ex.Message);
                throw new HubException(ex.Message);
            }

            if (updatedRoom != null)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, roomId);
                await Clients.Caller.SendAsync("JoinRoomSuccess", roomId);
                await BroadcastRoomAsync(updatedRoom);
                // 진행 중인 오목에 재접속(새로고침)한 흑에게 금수 자리 재전송
                if (updatedRoom.GameMode == GameMode.Omok && updatedRoom.Omok != null && !updatedRoom.Omok.IsFinished)
                    await SendForbiddenPointsToBlack(roomId, updatedRoom);
                await SendRoomListToAll();
            }
        }
    }

    // ✅ 방 나가기
    public async Task LeaveRoom(string roomId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room != null)
            await LeaveOneRoomAsync(room, PlayerKey, Clients, _context, _roomService, _omokService);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomId);
        await SendRoomListToAll();
    }

    public Task<object> GetRoom(string roomId)
    {
        var room = _roomService.GetRoom(roomId);
        if(room == null) throw new HubException("방을 찾을 수 없습니다.");

        // 초기 로드 shape를 RoomUpdated와 동일하게 통일 + 호출자 관점으로 마스킹
        return Task.FromResult<object>(_roomService.CreateViewFor(room, PlayerKey));
    }

    public async Task UpdateRoomSettings(string roomId, int maxRounds)
    {
        var room = _roomService.GetRoom(roomId);
        if (room == null) return;

        // 방장이 아닌 사람이 요청하면 무시. 게임 시작 후·범위 밖(1~10)도 무시
        if (room.HostPlayerId != PlayerKey) return;
        if (room.IsStarted) return;
        if (maxRounds < 1 || maxRounds > 10) return;

        // 서버 메모리에 저장된 라운드 수 변경
        room.MaxRounds = maxRounds;

        // 🔴 방의 모든 인원에게 변경된 정보를 쏨 (RoomUpdated 이벤트 발생)
        await BroadcastRoomAsync(room);
    }

    // 조커 갯수 변경 (0, 1, 2). 방장만 가능, 게임 시작 전에만
    public async Task UpdateJokerCount(string roomId, int jokerCount)
    {
        var room = _roomService.GetRoom(roomId);
        if (room == null) return;
        if (room.HostPlayerId != PlayerKey) return;
        if (room.IsStarted) return;
        if (jokerCount < 0 || jokerCount > 2) return;

        room.JokerCount = jokerCount;
        await BroadcastRoomAsync(room);
    }

    // 카드 모드 변경 (0=Trump, 1=Hwatu). 방장만 가능, 게임 시작 전에만
    public async Task UpdateCardMode(string roomId, int cardMode)
    {
        var room = _roomService.GetRoom(roomId);
        if (room == null) return;
        if (room.HostPlayerId != PlayerKey) return;
        if (room.IsStarted) return;
        if (!Enum.IsDefined(typeof(CardMode), cardMode)) return;

        room.CardMode = (CardMode)cardMode;
        await BroadcastRoomAsync(room);
    }

    // ✅ 채팅 — 로비 전체 (모든 클라이언트)
    public async Task SendChatMessage(string message)
    {
        var nickname = Context.User?.Identity?.Name ?? "Unknown";
        await Clients.All.SendAsync("ReceiveMessage", nickname, message);
    }

    // ✅ 채팅 — 특정 방 안에서만 (게임 중)
    public async Task SendRoomChat(string roomId, string message)
    {
        var nickname = Context.User?.Identity?.Name ?? "Unknown";
        await Clients.Group(roomId).SendAsync("ReceiveMessage", nickname, message);
    }

    private Task SendRoomListToAll() => SendRoomListToAll(Clients, _roomService);

    private static async Task SendRoomListToAll(IHubClients<IClientProxy> clients, GameRoomService roomService)
    {
        var roomList = roomService.GetRooms().Select(r => new {
            r.RoomId,
            r.Title,
            PlayerCount = r.Players.Count,
            r.IsStarted,
            IsLocked = !string.IsNullOrEmpty(r.Password)
        });
        await clients.All.SendAsync("RoomList", roomList);
    }

    // ✅ 내 프로필 가져오기 (Nickname 사용)
    public async Task<object?> GetMyProfile()
    {
        var identityName = Context.User?.Identity?.Name;
        if (string.IsNullOrEmpty(identityName)) return null;

        // DB에서 Nickname 컬럼을 조회
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Nickname == identityName);

        if (user == null) return null;

        return new
        {
            name = user.Nickname, // 클라이언트가 기대하는 JSON 필드명은 name으로 유지
            wins = user.Wins,
            totalGames = user.TotalGames,
            maxScore = user.MaxScore,
            minScore = user.MinScore
        };
    }
}
