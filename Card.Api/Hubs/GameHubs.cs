using Card.Api.Services;
using Card.Api.Domain;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using Card.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Card.Hubs;

[Authorize]
public class GameHub : Hub
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
                await clients.Group(roomId).SendAsync("OmokGameEnded", new
                {
                    Reason = room.Omok?.EndReason,
                    WinnerPlayerId = room.Omok?.WinnerPlayerId,
                    WinnerColor = room.Omok?.WinnerColor,
                    IsDraw = false,
                    WinningLine = new List<int[]>()
                });
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

    // ✅ [새로 추가한 기능] 게임 시작
    public async Task StartGame(string roomId, int maxRounds)
    {
        try
        {
            using (await _roomService.LockRoomAsync(roomId))
            {
                // 1. 서비스에서 게임 데이터 초기화 (패 배분, 첫 턴 설정)
                _roomService.StartGame(roomId, maxRounds);

                var room = _roomService.GetRoom(roomId);
                if (room == null) return;

                // 2. 모든 플레이어에게 "게임 시작"과 "첫 데이터"를 동시에 전송
                // GameStarted와 RoomUpdated를 연달아 보내 모든 클라이언트의 UI를 강제 갱신합니다.
                await BroadcastRoomAsync(room, "GameStarted");
                await BroadcastRoomAsync(room);

                Console.WriteLine($"Game Started in Room {roomId}. First Turn: {room.CurrentTurnPlayerId}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"StartGame Error: {ex.Message}");
            await Clients.Caller.SendAsync("ErrorMessage", "게임을 시작할 수 없습니다: " + ex.Message);
        }
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

    // 뻥 대기 시간(ms): 버린 뒤 이 시간 안에만 뻥 가능. 턴은 기다리지 않고 바로 넘어간다
    private const int PungWindowMs = 2000;

    public async Task PlayCard(string roomId, PlayingCard card)
    {
        string cardOwnerId = PlayerKey;
        try
        {
            int myWindowToken;

            // ── 1구간: 카드 버림 + 뻥 대기 시간 시작 + 턴 이동 (락 안에서 원자적으로) ──
            using (await _roomService.LockRoomAsync(roomId))
            {
                var room = _roomService.GetRoom(roomId);
                if (room == null || !room.IsStarted || room.IsFinished) return;
                // 턴 가드: 내 턴이 아니면 무시(중복/난입 방지)
                if (room.CurrentTurnPlayerId != cardOwnerId) return;

                // 현재 턴인 유저가 선택한 카드 1장을 버림 (서비스는 턴을 넘기지 않음)
                // 손에 없는 카드 등으로 실제 버림이 없으면 아무것도 진행하지 않는다 (턴 건너뛰기·가짜 윈도우 방지)
                if (!_roomService.PlayCard(roomId, cardOwnerId, card))
                {
                    await Clients.Caller.SendAsync("ErrorMessage", "버릴 수 없는 카드입니다.");
                    return;
                }

                // 바가지/STOP 등 즉시 라운드 종료된 경우 — 결과만 전파하고 종료
                if (room.IsRoundEnded || room.IsFinished)
                {
                    await BroadcastRoomAsync(room);
                    await SendResultBoardAsync(room);
                    await RecordGameResultAsync(room);
                    return;
                }

                // 뻥 대기 시간 시작 — 누가 무엇을 버리든 항상 연다.
                // (뻥 가능한 사람이 있을 때만 열면 "열렸다/안 열렸다"로 남의 손패 정보가 전원에게 새기 때문)
                // 대기 시간마다 고유 토큰: InterruptDiscard는 열린 토큰을 0으로 바꾼 한 명만 통과하고,
                // 마감(2구간)은 자기 토큰일 때만 닫아 뒤늦은 마감이 다음 대기 시간을 닫지 못하게 한다
                myWindowToken = Interlocked.Increment(ref room.PungWindowSeq);
                Interlocked.Exchange(ref room.PungWindowFlag, myWindowToken);
                room.PungWindowDraw = null;

                // 턴은 기다리지 않고 바로 넘긴다 — 다음 사람은 즉시 뽑을 수 있고,
                // 대기 시간 안에 뻥이 들어오면 그 사람이 뽑은 카드는 InterruptDiscard가 덱으로 되돌린다
                MoveToNextTurn(room, cardOwnerId);
                await BroadcastRoomAsync(room);
            }

            // 다른 유저가 InterruptDiscard를 호출할 시간. 락 밖 — 그래야 InterruptDiscard/DrawCard가 락을 잡고 들어올 수 있다.
            await Task.Delay(PungWindowMs);

            // ── 2구간: 윈도우 마감 (락 안에서) ──
            using (await _roomService.LockRoomAsync(roomId))
            {
                var room = _roomService.GetRoom(roomId);
                if (room == null) return;

                // 마감 시도: 내 토큰→0 성공이면 아무도 뻥하지 않은 것 (0이면 누가 뻥한 것, 다른 토큰이면 이미 다음 대기 시간)
                if (Interlocked.CompareExchange(ref room.PungWindowFlag, 0, myWindowToken) == myWindowToken)
                {
                    room.PungWindowDraw = null;      // 이 대기 시간 중의 뽑기는 확정
                    await BroadcastRoomAsync(room);  // 마감 전파 — 클라이언트 뻥 버튼이 꺼지도록
                }
                // 누가 뻥했으면 InterruptDiscard가 상태를 이미 전파했으므로 여기서는 아무것도 안 함
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"PlayCard Hub Error: {ex.Message}");
        }
    }

    // 라운드/게임 종료 결과창 신호 (바가지·STOP·승리선언·타임아웃 공용)
    private Task SendResultBoardAsync(GameRoom room) =>
        Clients.Group(room.RoomId).SendAsync("ShowResultBoard", new
        {
            WinnerPlayerId = room.WinnerPlayerId,
            WinnerName = room.WinnerName,
            WinnerHand = room.WinnerHand,
            WinType = room.LastWinType
        });

    // 턴 이동 보조 메서드
    private void MoveToNextTurn(GameRoom room, string currentPlayerId)
    {
        int currentIndex = room.Players.FindIndex(p => p.PlayerId == currentPlayerId);
        int nextIndex = (currentIndex + 1) % room.Players.Count;

        // 다음 플레이어 결정
        var nextPlayer = room.Players[nextIndex];
        room.CurrentTurnPlayerId = nextPlayer.PlayerId;
        room.TurnStartedAt = DateTime.UtcNow;  // 턴 타임아웃 기준 시각 갱신

        // 다음 턴을 시작할 플레이어의 턴 횟수 증가
        nextPlayer.RoundTurnCount++;
    }

    // 🔥 [뻥 액션] B가 버튼을 클릭했을 때 호출
    public async Task InterruptDiscard(string roomId)
    {
        try
        {
            using (await _roomService.LockRoomAsync(roomId))
            {
            var room = _roomService.GetRoom(roomId);
            if (room == null || room.IsRoundEnded) return;

            var targetCard = room.LastDiscardedCard;
            if (targetCard == null) return;

            var player = room.Players.FirstOrDefault(p => p.PlayerId == PlayerKey);
            if (player == null) return;

            // 자격 검증을 먼저 한 뒤, atomic하게 윈도우 닫기 시도 (한 명만 통과). 자기가 버린 카드는 뻥 불가
            if (room.LastActorPlayerId == player.PlayerId) return;
            if (!CheckCanIntercept(player, targetCard)) return;
            int openToken = Volatile.Read(ref room.PungWindowFlag);
            if (openToken == 0 || Interlocked.CompareExchange(ref room.PungWindowFlag, 0, openToken) != openToken) return;

            // --- 수정된 부분: 숫자 카드 우선순위 로직 ---
            // 1-1. 내 패에서 상대가 버린 카드와 같은 숫자의 인덱스들을 먼저 찾음
            var sameRankIndexes = player.Hand
                .Select((card, index) => new { card, index })
                .Where(x => x.card.Rank == targetCard.Rank)
                .Select(x => x.index)
                .ToList();

            // 1-2. 조커 카드들의 인덱스를 찾음
            var jokerIndexes = player.Hand
                .Select((card, index) => new { card, index })
                .Where(x => x.card.Rank == "Joker" || x.card.Rank == "JK")
                .Select(x => x.index)
                .ToList();

            // 1-3. 최종적으로 버릴 2개의 인덱스 결정 (숫자 우선 -> 조커 순)
            var handIndexes = new List<int>();
            
            // 숫자를 먼저 다 담음 (최대 2개)
            handIndexes.AddRange(sameRankIndexes.Take(2));

            // 아직 2개가 안 채워졌다면 조커로 채움
            if (handIndexes.Count < 2)
            {
                int needed = 2 - handIndexes.Count;
                handIndexes.AddRange(jokerIndexes.Take(needed));
            }
            // ------------------------------------------

            if (handIndexes.Count >= 2)
            {
                // 뻥 대기 시간 중에 다음 사람이 이미 뽑았으면 그 카드는 덱으로 되돌린다 (뽑은 사람이 봤으므로 섞어서 넣음)
                var reverted = _roomService.RevertPungWindowDraw(room, openToken);
                if (reverted != null)
                {
                    var drawerName = room.Players.FirstOrDefault(p => p.PlayerId == reverted.PlayerId)?.Name ?? reverted.PlayerId;
                    await Clients.Group(roomId).SendAsync("ErrorMessage", $"🔄 {drawerName}님이 방금 뽑은 카드는 뻥으로 덱에 돌아갔습니다 (덱 섞음)");
                }

                // 2. 서비스의 DiscardCards를 호출하여 2장을 '버려진 카드 더미'로 이동
                // (이 메서드 내부에서 room.DiscardPile.Add가 수행됨)
                _roomService.DiscardCards(room, player, handIndexes);

                // 3. 턴을 뻥 한 사람(나)으로 변경
                room.CurrentTurnPlayerId = player.PlayerId;
                room.TurnStartedAt = DateTime.UtcNow;       // 뻥한 사람의 턴 타이머 기준 갱신
                room.LastActorPlayerId = player.PlayerId;   // 바닥의 마지막 카드는 뻥한 사람이 버린 것
                player.RoundTurnCount++;    // 플레이어의 턴 횟수 확인(승리 선언 위함)

                await Clients.Group(roomId).SendAsync("ErrorMessage", $"🔥 {player.Name}님이 뻥을 성공했습니다!! 🔥");

                // 4. 상태 전파 (A가 버린 1장 + 내가 버린 2장이 바닥에 보임)
                await BroadcastRoomAsync(room);
                
                // 5. 클라이언트에게 1장 더 버리라고 신호 보냄
                await Clients.Caller.SendAsync("PungSuccess", "패에서 추가로 버릴 카드 1장을 선택하세요.");
            }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"InterruptDiscard Error: {ex.Message}");
        }
    }

    public async Task DeclareStop(string roomId)
    {
        using (await _roomService.LockRoomAsync(roomId))
        {
            // 서비스의 로직 호출
            _roomService.DeclareStop(roomId, PlayerKey);

            // 알림 메시지 전송
            var room = _roomService.GetRoom(roomId);
            var player = room?.Players.FirstOrDefault(p => p.PlayerId == PlayerKey);
            if (player != null && room.IsStopDeclared)
            {
                await Clients.Group(roomId).SendAsync("ErrorMessage", $"{player.Name}님이 STOP을 선언했습니다! 카드를 버리면 게임이 종료됩니다.");
                await BroadcastRoomAsync(room);
            }
        }
    }


    // 가로채기 가능 여부 판단 보조 메서드
    // 조커 식별은 GameRoomService.IsJoker로 통일
    private bool CheckCanIntercept(Player player, PlayingCard playedCard)
    {
        // 3.1 룰: 내가 2장(같은 숫자) 들고 있는데 남이 그 숫자를 냈을 때
        if (player.Hand.Count == 2)
        {
            bool isPair = player.Hand[0].Rank == player.Hand[1].Rank || player.Hand.Any(c => GameRoomService.IsJoker(c));
            bool matchesPlayed = player.Hand.Any(c => c.Rank == playedCard.Rank) || GameRoomService.IsJoker(playedCard);
            if (isPair && matchesPlayed) return true;
        }

        // 뻥 룰: 손패 5장이고 같은 랭크 카드 + 조커가 합쳐 2장 이상이면 뻥 가능
        // (뻥 시 같은 랭크 2장 우선, 부족분은 조커로 채움 — InterruptDiscard 처리부와 동일 정책)
        if (player.Hand.Count == 5)
        {
            int matchingRank = player.Hand.Count(c => c.Rank == playedCard.Rank);
            int jokers = player.Hand.Count(c => GameRoomService.IsJoker(c));
            if (matchingRank + jokers >= 2) return true;
        }

        return false;
    }

    public async Task DrawCard(string roomId)
    {
        using (await _roomService.LockRoomAsync(roomId))
        {
            int pileBefore = _roomService.GetRoom(roomId)?.DiscardPile.Count ?? 0;
            var updatedRoom = _roomService.DrawCard(roomId, PlayerKey);
            if (updatedRoom != null)
            {
                // 덱이 비어 버림더미가 자동으로 덱에 들어갔으면 알림
                if (updatedRoom.DiscardPile.Count < pileBefore)
                    await Clients.Group(roomId).SendAsync("ErrorMessage", "덱이 비어 버려진 카드를 다시 섞어 넣었습니다.");
                // RoundTurnCount는 Service.DrawCard에서 이미 증가시킴 (이중 증가 방지)
                await BroadcastRoomAsync(updatedRoom);
            }
        }
    }

    // 방장 수동 셔플 — 자동 보충의 예비 수단 (덱이 비어 있을 때만). 별도 이벤트 없이 알림 + RoomUpdated로 전파
    public async Task ReshuffleDeck(string roomId)
    {
        using (await _roomService.LockRoomAsync(roomId))
        {
            if (!_roomService.ReshuffleDiscardPile(roomId, PlayerKey)) return;
            var room = _roomService.GetRoom(roomId);
            if (room == null) return;
            await Clients.Group(roomId).SendAsync("ErrorMessage", "버려진 카드를 다시 섞어 덱에 넣었습니다.");
            await BroadcastRoomAsync(room);
        }
    }

    // 승리 로직 계산
    public async Task DeclareWin(string roomId)
    {
        using (await _roomService.LockRoomAsync(roomId))
        {
            var room = _roomService.GetRoom(roomId);
            if (room == null) return;

            var player = room.Players
                .FirstOrDefault(p => p.PlayerId == PlayerKey);
            if (player == null) return;

            // ✅ 1. 먼저 선언 가능 여부 검사
            if (!_roomService.CanDeclareWin(room, player))
            {
                await Clients.Caller.SendAsync(
                    "ErrorMessage",
                    "아직 승리 선언을 할 수 없습니다."
                );
                return;
            }

            // ✅ 2. 그 다음 실제 승리 처리
            bool success = _roomService.DeclareWin(
                room,
                player,
                WinReason.ManualDeclare
            );

            if (!success)
            {
                await Clients.Caller.SendAsync(
                    "ErrorMessage",
                    "조건에 맞지 않습니다."
                );
                return;
            }

            // ✅ 3. 결과 전파
            await SendResultBoardAsync(room);

            await BroadcastRoomAsync(room);
            await RecordGameResultAsync(room);
        }
    }

    public async Task RequestNextRound(string roomId)
    {
        try
        {
            using (await _roomService.LockRoomAsync(roomId))
            {
                var room = _roomService.GetRoom(roomId);
                if (room == null) return;

                // 방장만 다음 라운드를 시작할 수 있도록 권한 체크 추가
                if (room.HostPlayerId != PlayerKey)
                {
                    await Clients.Caller.SendAsync("ErrorMessage", "방장만 다음 라운드를 시작할 수 있습니다.");
                    return;
                }

                // 1. 서비스에서 덱 생성, '셔플', 카드 분배, 턴 설정을 모두 수행
                _roomService.StartNextRound(room);

                // 2. 모든 플레이어에게 전광판을 닫으라고 명령
                await Clients.Group(roomId).SendAsync("HideResultBoard");

                // 3. 갱신된 방 상태(새 패, 새로운 턴 등)를 전송 — 다른 핸들러와 동일하게 raw room 전송
                //    (기존엔 GetRoom 프로젝션을 보내 필드 누락으로 다음 라운드 상태가 꼬였음)
                await BroadcastRoomAsync(room);

                Console.WriteLine($"Next Round Started: {room.CurrentRound}. Turn: {room.CurrentTurnPlayerId}");
            }
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync("ErrorMessage", "라운드 전환 중 오류: " + ex.Message);
        }
    }

    // 기권
    public async Task GiveUp(string roomId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room == null) return;

        // 1. 기권 처리 (IsFinished = true 처리됨)
        _roomService.GiveUpGame(roomId, PlayerKey);

        // 2. ⭐ 명시적으로 게임 시작 상태를 해제 (대기실 복귀용)
        room.IsStarted = false; 

        // 3. 최신 데이터 전송
        await BroadcastRoomAsync(room);
        await RecordGameResultAsync(room);
        
        // 4. 전광판을 띄우는 대신 바로 나가게 하고 싶다면 이 신호를 보냄
        // 만약 결과 확인 후 나가게 하고 싶다면 "GameTerminated" 신호를 사용
        await Clients.Group(roomId).SendAsync("GameTerminated", roomId);
    }

    // ===== 오목 모드 =====

    // 오목 게임 시작 — 방장만, 정확히 2명일 때
    public async Task StartOmokGame(string roomId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room == null) return;
        if (room.HostPlayerId != PlayerKey)
        {
            await Clients.Caller.SendAsync("ErrorMessage", "방장만 게임을 시작할 수 있습니다.");
            return;
        }

        bool ok = _omokService.StartOmokGame(roomId);
        if (!ok)
        {
            await Clients.Caller.SendAsync("ErrorMessage", "오목 게임을 시작할 수 없습니다 (2명 필요).");
            return;
        }

        await BroadcastRoomAsync(room, "GameStarted");
        await BroadcastRoomAsync(room);
        await SendForbiddenPointsToBlack(roomId, room);
    }

    // 돌 놓기
    public async Task PlaceStone(string roomId, int row, int col)
    {
        var result = _omokService.PlaceStone(roomId, PlayerKey, row, col);
        if (!result.Success)
        {
            await Clients.Caller.SendAsync("ErrorMessage", result.Error ?? "돌을 놓을 수 없습니다.");
            return;
        }

        var room = _roomService.GetRoom(roomId);
        if (room == null) return;

        await BroadcastRoomAsync(room);

        if (result.GameEnded)
        {
            await Clients.Group(roomId).SendAsync("OmokGameEnded", new
            {
                Reason = result.EndReason,
                WinnerPlayerId = room.Omok?.WinnerPlayerId,
                WinnerColor = room.Omok?.WinnerColor,
                IsDraw = room.Omok?.IsDraw ?? false,
                WinningLine = room.Omok?.WinningLine
            });
        }
        else
        {
            // 다음 차례가 흑이면 흑에게만 금수 자리 송신 (백 차례에는 빈 리스트)
            await SendForbiddenPointsToBlack(roomId, room);
        }
    }

    // 흑 차례인 경우, 흑 ConnectionId에만 33 금수 자리 송신
    private async Task SendForbiddenPointsToBlack(string roomId, GameRoom room)
    {
        if (room.Omok == null) return;
        var points = _omokService.GetCurrentBlackForbiddenPoints(roomId);
        var blackId = room.Omok.BlackPlayerId;
        if (string.IsNullOrEmpty(blackId)) return;
        // PlayerId가 닉네임(=UserIdentifier)이므로 User 타겟 전송 — 재접속해도 도달
        await Clients.User(blackId).SendAsync("OmokForbiddenPoints", new { Points = points });
    }

    // 오목 기권
    public async Task ResignOmok(string roomId)
    {
        bool ok = _omokService.ResignOmok(roomId, PlayerKey);
        if (!ok) return;

        var room = _roomService.GetRoom(roomId);
        if (room == null) return;

        await BroadcastRoomAsync(room);
        await Clients.Group(roomId).SendAsync("OmokGameEnded", new
        {
            Reason = room.Omok?.EndReason,
            WinnerPlayerId = room.Omok?.WinnerPlayerId,
            WinnerColor = room.Omok?.WinnerColor,
            IsDraw = false,
            WinningLine = new List<int[]>()
        });
    }

    // ===== 뱅(Bang!) 모드 =====

    public async Task StartBangGame(string roomId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room == null) return;
        if (room.HostPlayerId != PlayerKey)
        {
            await Clients.Caller.SendAsync("ErrorMessage", "방장만 게임을 시작할 수 있습니다.");
            return;
        }
        bool ok = _bangService.StartBangGame(roomId);
        if (!ok)
        {
            await Clients.Caller.SendAsync("ErrorMessage", "뱅 게임을 시작할 수 없습니다 (4~7명 필요).");
            return;
        }
        await BroadcastRoomAsync(room, "GameStarted");
        await BroadcastRoomAsync(room);

        // 사람이 손패 확인할 시간 5초 대기 (보안관이 봇이라도)
        await Clients.Group(roomId).SendAsync("BangLog", "⏳ 게임 시작! 손패를 확인할 시간 5초...");
        await Task.Delay(5000);

        // 보안관이 봇이거나 첫 응답자 봇이면 자동 진행
        await TriggerBotActions(roomId);
    }

    // 뱅 — 봇 1명 추가. 방장만, 게임 시작 전, 최대 7명까지
    public async Task AddBangBot(string roomId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room == null) return;
        if (room.HostPlayerId != PlayerKey)
        {
            await Clients.Caller.SendAsync("ErrorMessage", "방장만 봇을 추가할 수 있습니다.");
            return;
        }
        if (room.IsStarted) return;
        if (room.GameMode != GameMode.Bang) return;
        var ok = _roomService.AddBot(roomId);
        if (!ok)
        {
            await Clients.Caller.SendAsync("ErrorMessage", "봇을 추가할 수 없습니다 (정원 초과 또는 게임 시작됨).");
            return;
        }
        await BroadcastRoomAsync(room);
    }

    // 뱅 — Dodge City 확장팩 사용 토글. 방장만, 게임 시작 전에만
    public async Task UpdateBangExpansion(string roomId, bool useDodgeCity)
    {
        var room = _roomService.GetRoom(roomId);
        if (room == null) return;
        if (room.HostPlayerId != PlayerKey) return;
        if (room.IsStarted) return;
        if (room.GameMode != GameMode.Bang) return;
        room.BangUseDodgeCity = useDodgeCity;
        await BroadcastRoomAsync(room);
    }

    // 뱅 — 응답 타이머 한도 설정. 방장만, 언제든 변경 가능 (전체 인원에게 통일 적용)
    public async Task UpdateBangResponseTimer(string roomId, int seconds)
    {
        var room = _roomService.GetRoom(roomId);
        if (room == null) return;
        if (room.HostPlayerId != PlayerKey) return;
        if (room.GameMode != GameMode.Bang) return;
        if (!new[] { 10, 20, 30, 60 }.Contains(seconds)) return;
        room.BangResponseTimerSec = seconds;
        await BroadcastRoomAsync(room);
    }

    // 재대국 — 같은 방에서 다시 시작. 방장만
    public async Task RestartBangGame(string roomId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room == null) return;
        if (room.HostPlayerId != PlayerKey)
        {
            await Clients.Caller.SendAsync("ErrorMessage", "방장만 재대국할 수 있습니다.");
            return;
        }
        if (room.GameMode != GameMode.Bang) return;
        // 게임 종료 상태에서만
        if (!room.IsFinished && room.IsStarted) return;
        // 상태 초기화
        room.Bang = null;
        room.IsStarted = false;
        room.IsFinished = false;

        bool ok = _bangService.StartBangGame(roomId);
        if (!ok)
        {
            await Clients.Caller.SendAsync("ErrorMessage", "재대국에 실패했습니다 (4~7명 필요).");
            return;
        }
        await BroadcastRoomAsync(room, "GameStarted");
        await BroadcastRoomAsync(room);
        await Clients.Group(roomId).SendAsync("BangLog", "🔄 재대국 시작!");
        await Clients.Group(roomId).SendAsync("BangLog", "⏳ 손패를 확인할 시간 5초...");
        await Task.Delay(5000);

        await TriggerBotActions(roomId);
    }

    // 봇 자동 액션 트리거 — 한 번에 1액션씩 + delay로 사용자 가시성 확보
    private async Task TriggerBotActions(string roomId)
    {
        const int maxIterations = 30;
        const int delayMs = 500;
        for (int i = 0; i < maxIterations; i++)
        {
            var (didAction, log) = _bangService.ProcessOneBotAction(roomId);
            if (!didAction)
            {
                // 봇이 멈춘 이유 진단 로그 (디버깅용)
                var reason = _bangService.GetBotStallReason(roomId);
                if (!string.IsNullOrEmpty(reason))
                    await Clients.Group(roomId).SendAsync("BangLog", $"[봇 대기] {reason}");
                break;
            }

            var updatedRoom = _roomService.GetRoom(roomId);
            if (updatedRoom != null) await BroadcastRoomAsync(updatedRoom);
            if (!string.IsNullOrEmpty(log)) await Clients.Group(roomId).SendAsync("BangLog", log);

            if (updatedRoom?.Bang?.IsFinished == true)
            {
                await Clients.Group(roomId).SendAsync("BangGameEnded", new
                {
                    WinnerRole = updatedRoom.Bang.WinnerRole,
                    WinnerPlayerIds = updatedRoom.Bang.WinnerPlayerIds,
                    Reason = updatedRoom.Bang.EndReason
                });
                break;
            }

            await Task.Delay(delayMs);
        }
    }

    // 카드 사용 (BANG/Beer/Stagecoach 등). targetPlayerId는 BANG처럼 대상이 필요한 경우만
    public async Task PlayBangCard(string roomId, int cardId, string? targetPlayerId)
    {
        var result = _bangService.PlayCard(roomId, PlayerKey, cardId, targetPlayerId);
        if (!result.Success)
        {
            await Clients.Caller.SendAsync("ErrorMessage", result.Error ?? "카드를 사용할 수 없습니다.");
            return;
        }
        var room = _roomService.GetRoom(roomId);
        if (room == null) return;
        await BroadcastRoomAsync(room);
        if (!string.IsNullOrEmpty(result.Message))
            await Clients.Group(roomId).SendAsync("BangLog", result.Message);
        if (result.GameEnded)
        {
            await Clients.Group(roomId).SendAsync("BangGameEnded", new
            {
                WinnerRole = room.Bang?.WinnerRole,
                WinnerPlayerIds = room.Bang?.WinnerPlayerIds,
                Reason = room.Bang?.EndReason
            });
        }
    }

    // General Store — 시계방향으로 카드 선택
    public async Task PickGeneralStoreCard(string roomId, int cardId)
    {
        var result = _bangService.PickFromGeneralStore(roomId, PlayerKey, cardId);
        if (!result.Success)
        {
            await Clients.Caller.SendAsync("ErrorMessage", result.Error ?? "카드를 선택할 수 없습니다.");
            return;
        }
        var room = _roomService.GetRoom(roomId);
        if (room == null) return;
        await BroadcastRoomAsync(room);
        if (!string.IsNullOrEmpty(result.Message))
            await Clients.Group(roomId).SendAsync("BangLog", result.Message);
    }

    // Indians 응답 — useBang=true면 BANG 카드로 응답, false면 데미지 수용
    public async Task RespondIndians(string roomId, bool useBang)
    {
        var result = _bangService.RespondIndians(roomId, PlayerKey, useBang);
        await DispatchBangResult(roomId, result);
    }

    // Gatling 응답 — useMissed=true면 Missed로 회피, false면 데미지 수용
    public async Task RespondGatling(string roomId, bool useMissed)
    {
        var result = _bangService.RespondGatling(roomId, PlayerKey, useMissed);
        await DispatchBangResult(roomId, result);
    }

    // Duel 응답 — useBang=true면 BANG 던지고 상대 차례, false면 1 데미지 + 종료
    public async Task RespondDuel(string roomId, bool useBang)
    {
        var result = _bangService.RespondDuel(roomId, PlayerKey, useBang);
        await DispatchBangResult(roomId, result);
    }

    // Sid Ketchum 능력 — 손패 2장 버려 HP 1 회복 (언제든)
    public async Task UseSidKetchum(string roomId, List<int> discardCardIds)
    {
        var result = _bangService.UseSidKetchum(roomId, PlayerKey, discardCardIds);
        await DispatchBangResult(roomId, result);
    }

    // Lucky Duke — 펴진 2장 중 1장 직접 선택
    public async Task ChooseLuckyDuke(string roomId, int chosenCardId)
    {
        var result = _bangService.ChooseLuckyDuke(roomId, PlayerKey, chosenCardId);
        await DispatchBangResult(roomId, result);
    }

    // Chuck Wengam 능력 — 본인 턴에 HP 1 잃고 카드 2장
    public async Task UseChuckWengam(string roomId)
    {
        var result = _bangService.UseChuckWengam(roomId, PlayerKey);
        await DispatchBangResult(roomId, result);
    }

    // Doc Holyday 능력 — 본인 턴에 손패 2장 버려서 BANG 효과 (턴당 1회)
    public async Task UseDocHolyday(string roomId, List<int> discardCardIds, string targetPlayerId)
    {
        var result = _bangService.UseDocHolyday(roomId, PlayerKey, discardCardIds, targetPlayerId);
        await DispatchBangResult(roomId, result);
    }

    // Kit Carlson — 펴진 3장 중 덱으로 돌릴 1장 선택
    public async Task ChooseKitCarlson(string roomId, int returnedCardId)
    {
        var result = _bangService.KitCarlsonChoice(roomId, PlayerKey, returnedCardId);
        await DispatchBangResult(roomId, result);
    }

    // Jesse Jones — fromDeck=true면 일반 드로우, false면 fromPlayerId 손에서 1장
    public async Task ChooseJesseJones(string roomId, bool fromDeck, string? fromPlayerId)
    {
        var result = _bangService.JesseJonesChoice(roomId, PlayerKey, fromDeck, fromPlayerId);
        await DispatchBangResult(roomId, result);
    }

    // Pedro Ramirez — fromDeck=true면 일반 드로우, false면 버린 카드 top 1장
    public async Task ChoosePedroRamirez(string roomId, bool fromDeck)
    {
        var result = _bangService.PedroRamirezChoice(roomId, PlayerKey, fromDeck);
        await DispatchBangResult(roomId, result);
    }

    // 응답 결과 공통 broadcast 헬퍼
    private async Task DispatchBangResult(string roomId, BangActionResult result)
    {
        if (!result.Success)
        {
            await Clients.Caller.SendAsync("ErrorMessage", result.Error ?? "응답할 수 없습니다.");
            return;
        }
        var room = _roomService.GetRoom(roomId);
        if (room == null) return;
        await BroadcastRoomAsync(room);
        if (!string.IsNullOrEmpty(result.Message))
            await Clients.Group(roomId).SendAsync("BangLog", result.Message);

        // Lucky Duke 펴기 결과 별도 로그 + 클리어
        if (room.Bang?.LastLuckyDukeReveal != null && room.Bang.LastLuckyDukeReveal.Count > 0)
        {
            var picks = string.Join(" / ", room.Bang.LastLuckyDukeReveal.Select(c =>
            {
                var s = c.Suit switch
                {
                    Card.Api.Domain.CardSuit.Hearts => "♥",
                    Card.Api.Domain.CardSuit.Diamonds => "♦",
                    Card.Api.Domain.CardSuit.Clubs => "♣",
                    Card.Api.Domain.CardSuit.Spades => "♠",
                    _ => "?"
                };
                var rank = c.Rank switch
                {
                    1 => "A", 11 => "J", 12 => "Q", 13 => "K",
                    _ => c.Rank.ToString()
                };
                return $"{s}{rank}";
            }));
            await Clients.Group(roomId).SendAsync("BangLog", $"🍀 Lucky Duke 펴기 [{picks}]");
            room.Bang.LastLuckyDukeReveal = null;
        }

        if (result.GameEnded)
        {
            await Clients.Group(roomId).SendAsync("BangGameEnded", new
            {
                WinnerRole = room.Bang?.WinnerRole,
                WinnerPlayerIds = room.Bang?.WinnerPlayerIds,
                Reason = room.Bang?.EndReason
            });
            return;
        }

        // 봇 자동 액션 처리 — 1액션씩 delay 루프
        await TriggerBotActions(roomId);
    }

    // CatBalou/Panic 사용자가 대상 손패의 카드 ID를 직접 선택
    public async Task PickBangCardFromTarget(string roomId, int targetCardId)
    {
        var result = _bangService.PickCardFromTarget(roomId, PlayerKey, targetCardId);
        if (!result.Success)
        {
            await Clients.Caller.SendAsync("ErrorMessage", result.Error ?? "카드를 선택할 수 없습니다.");
            return;
        }
        var room = _roomService.GetRoom(roomId);
        if (room == null) return;
        await BroadcastRoomAsync(room);
        if (!string.IsNullOrEmpty(result.Message))
            await Clients.Group(roomId).SendAsync("BangLog", result.Message);
    }

    // BANG 응답 — useMissed=true면 Missed로 회피, false면 데미지 수용
    public async Task RespondBang(string roomId, bool useMissed)
    {
        var result = _bangService.RespondBang(roomId, PlayerKey, useMissed);
        if (!result.Success)
        {
            await Clients.Caller.SendAsync("ErrorMessage", result.Error ?? "응답할 수 없습니다.");
            return;
        }
        var room = _roomService.GetRoom(roomId);
        if (room == null) return;
        await BroadcastRoomAsync(room);
        if (!string.IsNullOrEmpty(result.Message))
            await Clients.Group(roomId).SendAsync("BangLog", result.Message);
        if (result.GameEnded)
        {
            await Clients.Group(roomId).SendAsync("BangGameEnded", new
            {
                WinnerRole = room.Bang?.WinnerRole,
                WinnerPlayerIds = room.Bang?.WinnerPlayerIds,
                Reason = room.Bang?.EndReason
            });
        }
    }

    // 턴 종료. discardCardIds는 손패 초과 시 버릴 카드 ID 배열
    public async Task EndBangTurn(string roomId, List<int>? discardCardIds)
    {
        var result = _bangService.EndTurn(roomId, PlayerKey, discardCardIds);
        if (!result.Success)
        {
            await Clients.Caller.SendAsync("ErrorMessage", result.Error ?? "턴을 종료할 수 없습니다.");
            return;
        }
        var room = _roomService.GetRoom(roomId);
        if (room == null) return;
        await BroadcastRoomAsync(room);
        if (!string.IsNullOrEmpty(result.Message))
            await Clients.Group(roomId).SendAsync("BangLog", result.Message);
    }

    // ===== 턴 타임아웃 (모든 모드 공통) =====
    // 클라이언트 카운트다운이 만료되면 호출. 서버가 "현재 턴 == timedOutPlayerId" 이고
    // "마감시각 경과"임을 검증한 뒤에만 자동 진행한다. 검증 실패 시 무시 → 중복/조기 호출 방지.
    public async Task ForceTurnTimeout(string roomId, string timedOutPlayerId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room == null || !room.IsStarted || room.IsFinished) return;
        const int graceSec = 3;  // 클라/서버 시계 오차 유예

        // ── 오목 — 시간 초과한 사람 자동 기권(패배) ──
        if (room.GameMode == GameMode.Omok)
        {
            var omok = room.Omok;
            if (omok == null || omok.IsFinished) return;
            var currentId = omok.CurrentTurn == StoneColor.Black ? omok.BlackPlayerId : omok.WhitePlayerId;
            if (currentId != timedOutPlayerId) return;
            if ((DateTime.UtcNow - omok.TurnStartedAt).TotalSeconds < room.TurnTimeoutSec - graceSec) return;

            if (!_omokService.ResignOmok(roomId, currentId)) return;
            var r = _roomService.GetRoom(roomId);
            if (r == null) return;
            await BroadcastRoomAsync(r);
            await Clients.Group(roomId).SendAsync("OmokGameEnded", new
            {
                Reason = r.Omok?.EndReason,
                WinnerPlayerId = r.Omok?.WinnerPlayerId,
                WinnerColor = r.Omok?.WinnerColor,
                IsDraw = false,
                WinningLine = new List<int[]>()
            });
            return;
        }

        // ── 뱅 — 자동 턴 종료(초과 손패 자동 버림). 응답 대기 중은 응답 타이머가 처리 ──
        if (room.GameMode == GameMode.Bang)
        {
            var bang = room.Bang;
            if (bang == null || bang.IsFinished) return;
            if (bang.Pending != null) return;
            if (bang.CurrentTurnPlayerId != timedOutPlayerId) return;
            if ((DateTime.UtcNow - bang.TurnStartedAt).TotalSeconds < room.TurnTimeoutSec - graceSec) return;

            var me = bang.Players.FirstOrDefault(p => p.PlayerId == timedOutPlayerId);
            if (me == null) return;
            int excess = me.Hand.Count - me.Health;
            List<int>? discardIds = excess > 0 ? me.Hand.Take(excess).Select(c => c.Id).ToList() : null;

            var result = _bangService.EndTurn(roomId, timedOutPlayerId, discardIds);
            if (!result.Success) return;
            var r = _roomService.GetRoom(roomId);
            if (r != null) await BroadcastRoomAsync(r);
            await Clients.Group(roomId).SendAsync("BangLog", $"⏱ {me.Name} 턴 시간 초과 — 자동 종료");
            await TriggerBotActions(roomId);
            return;
        }

        // ── Cards — 자동으로 카드 1장 버려 턴 종료 (타임아웃 시 뻥 윈도우는 생략) ──
        if (room.GameMode == GameMode.Cards)
        {
            // 락 안에서 재검증 → 여러 클라가 동시에 호출해도 턴이 중복 진행되지 않음(TOCTOU 방지)
            using (await _roomService.LockRoomAsync(roomId))
            {
                var r = _roomService.GetRoom(roomId);
                if (r == null || !r.IsStarted || r.IsFinished) return;
                if (r.IsRoundEnded) return;
                if (r.CurrentTurnPlayerId != timedOutPlayerId) return;
                if ((DateTime.UtcNow - r.TurnStartedAt).TotalSeconds < r.TurnTimeoutSec - graceSec) return;

                var player = r.Players.FirstOrDefault(p => p.PlayerId == timedOutPlayerId);
                if (player == null) return;

                // 아직 안 뽑았으면(2·5장) 먼저 뽑는다 — 손패 장수 규칙(2/5 → 뽑아 3/6 → 1장 버림)을 지켜 1·4장이 되지 않게
                if (player.Hand.Count == 2 || player.Hand.Count == 5)
                    _roomService.DrawCard(roomId, timedOutPlayerId);

                // 버릴 수 있는 상태(3·6장)가 아니면(덱·버림더미 모두 소진 등) 카드 손실 없이 턴만 넘김
                if (player.Hand.Count == 3 || player.Hand.Count == 6)
                {
                    // 버림은 서비스 PlayCard로 — 바가지·STOP 정산 포함. 타임아웃 시 뻥 윈도우는 생략
                    _roomService.PlayCard(roomId, timedOutPlayerId, player.Hand[^1]);
                    if (r.IsRoundEnded || r.IsFinished)
                    {
                        await BroadcastRoomAsync(r);
                        await SendResultBoardAsync(r);
                        await RecordGameResultAsync(r);
                        return;
                    }
                }
                MoveToNextTurn(r, timedOutPlayerId);
                await BroadcastRoomAsync(r);
                await Clients.Group(roomId).SendAsync("ErrorMessage", $"⏱ {player.Name}님 턴 시간 초과 — 자동으로 카드를 버렸습니다.");
            }
            return;
        }
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

    // ✅ 전적 반영 — Cards 게임 종료 시 서버가 1회만 호출 (StatsRecorded 플래그로 중복 집계 방지)
    //    Wins는 게임 전체 승자(총점 최저) 기준, MaxScore/MinScore는 최종 총점 기준
    private Task RecordGameResultAsync(GameRoom room) => RecordGameResultAsync(room, _context, Clients);

    private static async Task RecordGameResultAsync(GameRoom room, GameDbContext db, IHubClients<IClientProxy> clients)
    {
        if (!room.IsFinished || room.StatsRecorded) return;
        room.StatsRecorded = true;

        foreach (var p in room.Players)
        {
            if (p.IsBot) continue;
            // Player의 Name과 User의 Nickname을 비교
            var user = await db.Users
                .FirstOrDefaultAsync(u => u.Nickname == p.Name);
            if (user == null) continue;

            user.TotalGames += 1;
            if (room.GameWinnerIds.Contains(p.PlayerId)) user.Wins += 1;
            if (p.TotalScore > user.MaxScore) user.MaxScore = p.TotalScore;
            // 첫 게임이면 그대로 기록, 이후엔 최저값 갱신 (DB 기본값 0을 최저점으로 오인하지 않도록)
            if (user.TotalGames == 1 || p.TotalScore < user.MinScore) user.MinScore = p.TotalScore;
        }

        // SQLite DB에 변경사항 저장
        await db.SaveChangesAsync();

        // 유저 스탯이 갱신되었음을 클라이언트에 알림
        await clients.Group(room.RoomId).SendAsync("UpdateUserStats");
    }

    public async Task ToggleNaturalBagaji(string roomId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room == null) return;

        var player = room.Players
            .FirstOrDefault(p => p.PlayerId == PlayerKey);
        if (player == null) return;

        player.AllowNaturalBagaji = !player.AllowNaturalBagaji;

        // 본인 UI만 갱신
        await Clients.Caller.SendAsync(
            "NaturalBagajiToggled",
            player.AllowNaturalBagaji
        );

        // 룸 상태 동기화 필요하면
        // await BroadcastRoomAsync(room);
    }
}