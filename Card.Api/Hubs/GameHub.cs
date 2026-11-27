using Card.Api.Services;
using Card.Api.Domain;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using Card.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Card.Hubs;

[Authorize]
public partial class GameHub : Hub
{
    private readonly GameRoomService _roomService;
    private readonly GameDbContext _context;
    private readonly OmokGameService _omokService;
    private readonly BangGameService _bangService;
    private readonly IHubContext<GameHub> _hubContext;      // 허브 인스턴스 밖(연결 끊김 유예 작업)에서 전송용
    private readonly IServiceScopeFactory _scopeFactory;    // 유예 작업에서 scoped DbContext를 만들기 위함

    public GameHub(GameRoomService roomService, GameDbContext context, OmokGameService omokService, BangGameService bangService,
                   IHubContext<GameHub> hubContext, IServiceScopeFactory scopeFactory)
    {
        _roomService = roomService;
        _context = context;
        _omokService = omokService;
        _bangService = bangService;
        _hubContext = hubContext;
        _scopeFactory = scopeFactory;
    }

    // ── 접속 추적: 마지막 연결이 끊기면 유예 시간 뒤 모든 방에서 자동 퇴장 (유령 플레이어·유령 방 방지) ──
    public override async Task OnConnectedAsync()
    {
        _roomService.RegisterConnection(PlayerKey);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // 허브 인스턴스는 곧 폐기되므로 유예 작업에 필요한 것만 캡처
        var key = PlayerKey;
        var hubContext = _hubContext;
        var scopeFactory = _scopeFactory;
        var roomService = _roomService;
        var omokService = _omokService;
        _ = roomService.UnregisterConnection(key, async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
                int left = await LeaveAllRoomsAsync(key, hubContext.Clients, db, roomService, omokService);
                if (left > 0) await SendRoomListToAll(hubContext.Clients, roomService);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Disconnect cleanup error ({key}): {ex.Message}");
            }
        });
        await base.OnDisconnectedAsync(exception);
    }

    // 🔑 안정 플레이어 신원 키(닉네임, JWT Name 클레임).
    // ConnectionId는 재접속마다 바뀌므로 신원으로 쓰지 않는다 — 그룹/연결 관리에만 사용.
    private string PlayerKey => Context.User?.Identity?.Name ?? "Unknown";

    // 🔒 방 전원에게 각자 관점으로 마스킹된 방 상태를 전송.
    // 그룹 브로드캐스트 대체 — 다른 플레이어의 손패/뱅 역할 등 비공개 정보 보호.
    private Task BroadcastRoomAsync(GameRoom room, string eventName = "RoomUpdated") => BroadcastRoomAsync(Clients, room, _roomService, eventName);

    // 뷰는 서비스의 CreateViewFor로 만든다 — 상대 손패 마스킹 + 본인에게만 족보 힌트
    private static async Task BroadcastRoomAsync(IHubClients<IClientProxy> clients, GameRoom room, GameRoomService roomService, string eventName = "RoomUpdated")
    {
        foreach (var p in room.Players)
        {
            if (p.IsBot) continue;
            await clients.User(p.PlayerId).SendAsync(eventName, roomService.CreateViewFor(room, p.PlayerId));
        }
    }

    // ── 퇴장 공통 처리 (방 나가기 / 로비 진입 / 연결 끊김 유예 만료) ──
    // 진행 중 오목의 대국자면 기권 처리, Cards는 서비스가 턴 이동·종료를 정리. 방이 남아 있으면 상태를 전파한다.
    private static async Task LeaveOneRoomAsync(GameRoom room, string playerKey, IHubClients<IClientProxy> clients, GameDbContext db,
                                                GameRoomService roomService, OmokGameService omokService)
    {
        var roomId = room.RoomId;
        using (await roomService.LockRoomAsync(roomId))
        {
            bool omokEnded = room.GameMode == GameMode.Omok && room.Omok != null && !room.Omok.IsFinished
                             && omokService.ResignOmok(roomId, playerKey, "상대 퇴장");
            bool deleted = roomService.LeaveRoom(roomId, playerKey);
            if (deleted) return;

            await BroadcastRoomAsync(clients, room, roomService);
            if (omokEnded)
            {
                await SendOmokResignedAsync(clients, room);
            }
            if (room.GameMode == GameMode.Cards && room.IsFinished)
                await RecordGameResultAsync(room, db, clients);
        }
    }

    /// <summary>플레이어를 들어 있는 모든 방에서 퇴장시킨다. 반환: 처리한 방 수.</summary>
    private static async Task<int> LeaveAllRoomsAsync(string playerKey, IHubClients<IClientProxy> clients, GameDbContext db,
                                                      GameRoomService roomService, OmokGameService omokService)
    {
        var rooms = roomService.GetRoomsOf(playerKey);
        foreach (var room in rooms)
            await LeaveOneRoomAsync(room, playerKey, clients, db, roomService, omokService);
        return rooms.Count;
    }

    // ===== 턴 타임아웃 (모든 모드 공통) =====
    // 클라이언트 카운트다운이 만료되면 호출. 서버가 "현재 턴 == timedOutPlayerId" 이고
    // "마감시각 경과"임을 검증한 뒤에만 자동 진행한다. 검증 실패 시 무시 → 중복/조기 호출 방지.
    // 모드별 처리는 각 모드 파일의 *TurnTimeoutAsync
    public async Task ForceTurnTimeout(string roomId, string timedOutPlayerId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room == null || !room.IsStarted || room.IsFinished) return;

        switch (room.GameMode)
        {
            case GameMode.Omok: await OmokTurnTimeoutAsync(roomId, room, timedOutPlayerId); break;
            case GameMode.Bang: await BangTurnTimeoutAsync(roomId, room, timedOutPlayerId); break;
            case GameMode.Cards: await CardsTurnTimeoutAsync(roomId, timedOutPlayerId); break;
        }
    }

    private const int TurnTimeoutGraceSec = 3;  // 클라/서버 시계 오차 유예
}
