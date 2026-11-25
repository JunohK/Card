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
    private readonly PlayerConnectionService _connService;
    private readonly GameDbContext _context;
    private readonly OmokGameService _omokService;
    private readonly BangGameService _bangService;

    public GameHub(GameRoomService roomService, PlayerConnectionService connService, GameDbContext context, OmokGameService omokService, BangGameService bangService)
    {
        _roomService = roomService;
        _connService = connService;
        _context = context;
        _omokService = omokService;
        _bangService = bangService;
    }

    // 🔑 안정 플레이어 신원 키(닉네임, JWT Name 클레임).
    // ConnectionId는 재접속마다 바뀌므로 신원으로 쓰지 않는다 — 그룹/연결 관리에만 사용.
    private string PlayerKey => Context.User?.Identity?.Name ?? "Unknown";

    // 🔒 방 전원에게 각자 관점으로 마스킹된 방 상태를 전송.
    // 그룹 브로드캐스트 대체 — 다른 플레이어의 손패/뱅 역할 등 비공개 정보 보호.
    private async Task BroadcastRoomAsync(GameRoom room, string eventName = "RoomUpdated")
    {
        foreach (var p in room.Players)
        {
            if (p.IsBot) continue;
            await Clients.User(p.PlayerId).SendAsync(eventName, room.CreateViewFor(p.PlayerId));
        }
    }

    // ✅ 로비 진입 (프로필 객체 전송 및 목록 갱신)
    public async Task EnterLobby()
    {
        var nickname = Context.User?.Identity?.Name ?? "Unknown";

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
                await SendRoomListToAll();
            }
        }
    }

    // ✅ 방 나가기
    public async Task LeaveRoom(string roomId)
    {
        using (await _roomService.LockRoomAsync(roomId))
        {
            _roomService.LeaveRoom(roomId, PlayerKey);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomId);

            var room = _roomService.GetRoom(roomId);
            if (room != null)
                await BroadcastRoomAsync(room);

            await SendRoomListToAll();
        }
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
        return Task.FromResult<object>(room.CreateViewFor(PlayerKey));
    }

    public async Task UpdateRoomSettings(string roomId, int maxRounds)
    {
        var room = _roomService.GetRoom(roomId);
        if (room == null) return;

        // 방장이 아닌 사람이 요청하면 무시
        if (room.HostPlayerId != PlayerKey) return;

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

    public async Task PlayCard(string roomId, PlayingCard card)
    {
        string cardOwnerId = PlayerKey;
        try
        {
            bool canPung;

            // ── 1구간: 카드 버림 + 뻥 윈도우 개시 (락 안에서 원자적으로) ──
            using (await _roomService.LockRoomAsync(roomId))
            {
                var room = _roomService.GetRoom(roomId);
                if (room == null || !room.IsStarted || room.IsFinished) return;
                // 턴 가드: 내 턴이 아니면 무시(중복/난입 방지)
                if (room.CurrentTurnPlayerId != cardOwnerId) return;

                // 현재 턴인 유저가 선택한 카드 1장을 버림 (서비스는 턴을 넘기지 않음)
                _roomService.PlayCard(roomId, cardOwnerId, card);

                // 즉시 전송하여 바닥에 카드가 깔린 것을 모두에게 보여줌
                await BroadcastRoomAsync(room);

                // 바가지/STOP 등 즉시 라운드 종료된 경우 — 결과만 전파하고 종료
                if (room.IsRoundEnded || room.IsFinished)
                {
                    await Clients.Group(roomId).SendAsync("ShowResultBoard", new
                    {
                        WinnerPlayerId = room.WinnerPlayerId,
                        WinnerName = room.WinnerName,
                        WinnerHand = room.WinnerHand,
                        WinType = room.LastWinType
                    });
                    return;
                }

                // 뻥(내 패에 동일 숫자 2장 보유) 가능 유저 체크
                canPung = room.Players.Any(p => p.PlayerId != cardOwnerId && CheckCanIntercept(p, card));
                if (canPung)
                {
                    // 뻥 윈도우 atomic open — InterruptDiscard가 이 안에서 단 1명만 통과
                    Interlocked.Exchange(ref room.PungWindowFlag, 1);
                }
            }

            if (!canPung)
            {
                // 뻥칠 사람이 없으면 즉시 다음 사람 턴으로 (락 안에서 턴 이동)
                using (await _roomService.LockRoomAsync(roomId))
                {
                    var room = _roomService.GetRoom(roomId);
                    if (room == null) return;
                    MoveToNextTurn(room, cardOwnerId);
                    await BroadcastRoomAsync(room);
                }
                return;
            }

            // 🔥 3초 대기: 다른 유저가 InterruptDiscard를 호출할 시간을 줌.
            //    이 구간은 락 밖 — 그래야 InterruptDiscard가 락을 잡고 들어올 수 있다.
            await Task.Delay(3000);

            // ── 2구간: 윈도우 마감 (락 안에서) ──
            using (await _roomService.LockRoomAsync(roomId))
            {
                var room = _roomService.GetRoom(roomId);
                if (room == null) return;

                // 윈도우 close 시도: 1→0 성공이면 아무도 안 가로챈 것
                bool noPungOccurred = Interlocked.CompareExchange(ref room.PungWindowFlag, 0, 1) == 1;

                if (noPungOccurred && !room.IsRoundEnded && !room.IsFinished && room.CurrentTurnPlayerId == cardOwnerId)
                {
                    MoveToNextTurn(room, cardOwnerId);
                    await BroadcastRoomAsync(room);
                }
                // 누가 가로챘으면 InterruptDiscard가 turn/결과를 이미 전파했으므로 여기서는 아무것도 안 함
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"PlayCard Hub Error: {ex.Message}");
        }
    }

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

            // 자격 검증을 먼저 한 뒤, atomic하게 윈도우 닫기 시도 (한 명만 통과)
            if (!CheckCanIntercept(player, targetCard)) return;
            if (Interlocked.CompareExchange(ref room.PungWindowFlag, 0, 1) != 1) return;

            // --- [다른 플레이어의 6장 또는 3장 패 회수 로직] ---
            foreach (var p in room.Players)
            {
                // 뻥을 한 본인이 아니고, 이미 카드를 뽑아 패가 6장 혹은 3장인 플레이어를 찾음
                if (p.PlayerId != player.PlayerId && (p.Hand.Count == 6 || p.Hand.Count == 3))
                {
                    // 마지막으로 뽑은 카드를 패에서 제거
                    var lastDrawnCard = p.Hand.Last();
                    p.Hand.RemoveAt(p.Hand.Count - 1);
                    // 제거한 카드를 덱의 맨 위로 다시 삽입
                    room.Deck.Insert(0, lastDrawnCard);
                }
            }

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
                // 2. 서비스의 DiscardCards를 호출하여 2장을 '버려진 카드 더미'로 이동
                // (이 메서드 내부에서 room.DiscardPile.Add가 수행됨)
                _roomService.DiscardCards(room, player, handIndexes);

                // 3. 턴을 뻥 한 사람(나)으로 변경
                room.CurrentTurnPlayerId = player.PlayerId;
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

    public async Task DeclarePung(string roomId)
    {
        using (await _roomService.LockRoomAsync(roomId))
        {
            // 서비스에서 자격 검증 후 성공 시에만 알림
            bool success = _roomService.DeclarePung(roomId, PlayerKey);
            if (!success)
            {
                await Clients.Caller.SendAsync("ErrorMessage", "뻥 조건을 만족하지 않습니다.");
                return;
            }

            var room = _roomService.GetRoom(roomId);
            var player = room?.Players.FirstOrDefault(p => p.PlayerId == PlayerKey);

            if (player != null)
            {
                await Clients.Group(roomId).SendAsync("ErrorMessage", $"{player.Name}님이 뻥을 선언했습니다!! ");
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
            var updatedRoom = _roomService.DrawCard(roomId, PlayerKey);
            if (updatedRoom != null)
            {
                // RoundTurnCount는 Service.DrawCard에서 이미 증가시킴 (이중 증가 방지)
                await BroadcastRoomAsync(updatedRoom);
            }
        }
    }

    public async Task ReshuffleDeck(string roomId)
    {
        var playerId = PlayerKey;
        var success = _roomService.ReshuffleDiscardPile(roomId, playerId);

        if (success)
        {
            // 방 안의 모든 유저에게 덱이 갱신되었음을 알림
            await Clients.Group(roomId).SendAsync("ReshuffleDeck", "버려진 카드가 다시 덱으로 들어갔습니다.");
            
            // 갱신된 방 상태 전송 (덱 개수 등을 클라이언트에서 업데이트하기 위함)
            var room = _roomService.GetRoom(roomId);
            if (room != null) await BroadcastRoomAsync(room, "ReshuffleDeck");
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
            await Clients.Group(roomId).SendAsync(
                "ShowResultBoard",
                new
                {
                    WinnerPlayerId = room.WinnerPlayerId,
                    WinnerName = room.WinnerName,
                    WinnerHand = room.WinnerHand,
                    WinType = room.LastWinType
                }
            );

            await BroadcastRoomAsync(room);
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
                if (player == null || player.Hand.Count == 0) return;

                var discard = player.Hand[^1];
                player.Hand.RemoveAt(player.Hand.Count - 1);
                r.LastDiscardedCard = discard;
                r.DiscardPile.Add(discard);
                r.LastActorPlayerId = timedOutPlayerId;
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

    private async Task SendRoomListToAll()
    {
        var roomList = _roomService.GetRooms().Select(r => new {
            r.RoomId,
            r.Title,
            PlayerCount = r.Players.Count,
            r.IsStarted,
            IsLocked = !string.IsNullOrEmpty(r.Password)
        });
        await Clients.All.SendAsync("RoomList", roomList);
    }

    public override async Task OnConnectedAsync()
    {
        var nickname = Context.User?.Identity?.Name ?? "Unknown";
        _connService.Bind(Context.ConnectionId, nickname);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _connService.Unbind(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
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

    // ✅ 게임 결과 업데이트 (Nickname 사용)
    public async Task UpdateGameResult(string roomId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room == null || !room.IsFinished) return;

        foreach (var p in room.Players)
        {
            // Player의 Name과 User의 Nickname을 비교
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Nickname == p.Name);

            if (user != null)
            {
                user.TotalGames += 1;

                if (p.Name == room.WinnerName)
                {
                    user.Wins += 1;
                }
                
                // 스코어 기록 업데이트 로직
                if (p.TotalScore > user.MaxScore) user.MaxScore = p.TotalScore;
            }
        }

        // SQLite DB에 변경사항 저장
        await _context.SaveChangesAsync();
        
        // 유저 스탯이 갱신되었음을 클라이언트에 알림
        await Clients.Group(roomId).SendAsync("UpdateUserStats");
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