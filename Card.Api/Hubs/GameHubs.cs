using Card.Api.Services;
using Card.Api.Domain;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using Card.Api.GameLogic;
using Card.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using SQLitePCL;

namespace Card.Hubs;

[Authorize]
public class GameHub : Hub
{
    private readonly GameRoomService _roomService;
    private readonly PlayerConnectionService _connService;
    private readonly GameDbContext _context;

    public GameHub(GameRoomService roomService, PlayerConnectionService connService, GameDbContext context)
    {
        _roomService = roomService;
        _connService = connService;
        _context = context;
    }

    // ✅ 로비 진입 (프로필 통계 전송 및 목록 갱신)
    public async Task EnterLobby()
    {
        var profile = await GetMyProfile();
        if (profile != null)
        {
            await Clients.Caller.SendAsync("ConnectedUser", profile);
        }
        else
        {
            // DB에 사용자 없음/미인증 fallback
            var nickname = Context.User?.Identity?.Name ?? "Unknown";
            await Clients.Caller.SendAsync("ConnectedUser", nickname);
        }
        await SendRoomListToAll();
    }

    // ✅ 방 생성
    public async Task CreateRoom(string title, string? password)
    {
        var nickname = Context.User?.Identity?.Name ?? "Unknown";
        var room = _roomService.CreateRoom(nickname, title, password);
        
        // 방 생성 직후 입장 처리
        _roomService.JoinRoom(room.RoomId, Context.ConnectionId, nickname, password);

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

        // 2. 닉네임 추출 (이 부분이 서비스 로직과 맞아야 합니다)
        var nickname = Context.User?.Identity?.Name ?? "Unknown";

        // 3. 서비스 호출 (비밀번호가 틀리거나 인원이 꽉 차면 여기서 예외가 발생할 수 있음)
        // 기존에 잘 되던 서비스 코드를 그대로 타게 합니다.
        var updatedRoom = _roomService.JoinRoom(roomId, Context.ConnectionId, nickname, password);

        if (updatedRoom != null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, roomId);
            await Clients.Caller.SendAsync("JoinRoomSuccess", roomId);
            await Clients.Group(roomId).SendAsync("RoomUpdated", updatedRoom);
            await SendRoomListToAll();
        }
    }

    // ✅ 방 나가기
    public async Task LeaveRoom(string roomId)
    {
        _roomService.LeaveRoom(roomId, Context.ConnectionId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomId);
        
        var room = _roomService.GetRoom(roomId);
        if (room != null)
            await Clients.Group(roomId).SendAsync("RoomUpdated", room);
            
        await SendRoomListToAll();
    }

    // ✅ [새로 추가한 기능] 게임 시작
    public async Task StartGame(string roomId, int maxRounds)
    {
        try 
        {
            // 1. 서비스에서 게임 데이터 초기화 (패 배분, 첫 턴 설정)
            _roomService.StartGame(roomId, maxRounds); 
            
            var room = _roomService.GetRoom(roomId);
            if (room == null) return;

            // 2. 모든 플레이어에게 "게임 시작"과 "첫 데이터"를 동시에 전송
            // GameStarted와 RoomUpdated를 연달아 보내 모든 클라이언트의 UI를 강제 갱신합니다.
            await Clients.Group(roomId).SendAsync("GameStarted", room);
            await Clients.Group(roomId).SendAsync("RoomUpdated", room);
            
            Console.WriteLine($"Game Started in Room {roomId}. First Turn: {room.CurrentTurnPlayerId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"StartGame Error: {ex.Message}");
            await Clients.Caller.SendAsync("ErrorMessage", "게임을 시작할 수 없습니다: " + ex.Message);
        }
    }

    public async Task<object> GetRoom(string roomId)
    {
        var room = _roomService.GetRoom(roomId);
        if(room == null) throw new HubException("방을 찾을 수 없습니다.");

        return new
        {
            RoomId = room.RoomId,
            Title = room.Title,
            Players = room.Players.Select(p => new {
                PlayerId = p.PlayerId,
                Name = p.Name,
                Hand = p.Hand ?? new List<PlayingCard>(),
                TotalScore = p.TotalScore
            }),
            CurrentTurnPlayerId = room.CurrentTurnPlayerId,
            LastDiscardedCard = room.LastDiscardedCard,
            DeckCount = room.DeckCount,
            IsStarted = room.IsStarted,   // 대기실 복귀 판단 기준
            IsFinished = room.IsFinished, // 전광판 표시 기준
            WinnerName = room.WinnerName,
            WinnerHand = room.WinnerHand ?? new List<PlayingCard>(),
            HostPlayerId = room.HostPlayerId,
            CurrentRound = room.CurrentRound,
            MaxRounds = room.MaxRounds
        };
    }

    public async Task UpdateRoomSettings(string roomId, int maxRounds)
    {
        var room = _roomService.GetRoom(roomId);
        if (room == null) return;

        // 방장이 아닌 사람이 요청하면 무시
        if (room.HostPlayerId != Context.ConnectionId) return;

        // 서버 메모리에 저장된 라운드 수 변경
        room.MaxRounds = maxRounds;

        // 🔴 방의 모든 인원에게 변경된 정보를 쏨 (RoomUpdated 이벤트 발생)
        await Clients.Group(roomId).SendAsync("RoomUpdated", room);
    }

    public async Task PlayCard(string roomId, PlayingCard card)
    {
        try
        {
            var room = _roomService.GetRoom(roomId);
            if (room == null || !room.IsStarted || room.IsFinished) return;

            string cardOwnerId = Context.ConnectionId;

            // 1. 현재 턴인 유저가 선택한 카드 1장을 버림 (서비스 호출)
            // 서비스의 PlayCard 내부에서는 턴을 넘기지 않도록 수정된 상태여야 합니다.
            _roomService.PlayCard(roomId, cardOwnerId, card);

            // 2. 즉시 전송하여 바닥에 카드가 깔린 것을 모두에게 보여줌
            await Clients.Group(roomId).SendAsync("RoomUpdated", room);

            // 3. 뻥(내 패에 동일 숫자 2장 보유) 가능 유저 체크
            var canPung = room.Players.Any(p => p.PlayerId != cardOwnerId && CheckCanIntercept(p, card));

            if (canPung)
            {
                // 🔥 3초 대기: 다른 유저가 InterruptDiscard를 호출할 시간을 줌
                await Task.Delay(3000); 

                // 3초 후 체크: 아무도 뻥을 안 해서 턴이 그대로라면 그때 다음 사람으로 넘김
                if (room.CurrentTurnPlayerId == cardOwnerId)
                {
                    MoveToNextTurn(room, cardOwnerId);
                    await Clients.Group(roomId).SendAsync("RoomUpdated", room);
                }
            }
            else
            {
                // 뻥칠 사람이 없으면 즉시 다음 사람 턴으로
                MoveToNextTurn(room, cardOwnerId);
                await Clients.Group(roomId).SendAsync("RoomUpdated", room);
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

        // 다음 턴을 시작할 플레이어의 턴 횟수 증가
        nextPlayer.RoundTurnCount++;
    }

    // 🔥 [뻥 액션] B가 버튼을 클릭했을 때 호출
    public async Task InterruptDiscard(string roomId)
    {
        try
        {
            var room = _roomService.GetRoom(roomId);
            if (room == null || room.IsRoundEnded) return;

            var targetCard = room.LastDiscardedCard; 
            if (targetCard == null) return;

            var player = room.Players.FirstOrDefault(p => p.PlayerId == Context.ConnectionId);
            if (player == null) return;

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
                await Clients.Group(roomId).SendAsync("RoomUpdated", room);
                
                // 5. 클라이언트에게 1장 더 버리라고 신호 보냄
                await Clients.Caller.SendAsync("PungSuccess", "패에서 추가로 버릴 카드 1장을 선택하세요.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"InterruptDiscard Error: {ex.Message}");
        }
    }

    public async Task DeclareStop(string roomId)
    {
        // 서비스의 로직 호출
        _roomService.DeclareStop(roomId, Context.ConnectionId);
        
        // 알림 메시지 전송
        var room = _roomService.GetRoom(roomId);
        var player = room?.Players.FirstOrDefault(p => p.PlayerId == Context.ConnectionId);
        if (player != null && room.IsStopDeclared)
        {
            await Clients.Group(roomId).SendAsync("ErrorMessage", $"{player.Name}님이 STOP을 선언했습니다! 카드를 버리면 게임이 종료됩니다.");
            await Clients.Group(roomId).SendAsync("RoomUpdated", room);
        }
    }

    public async Task DeclarePung(string roomId)
    {
        // 서비스의 로직 호출
        _roomService.DeclarePung(roomId, Context.ConnectionId);

        var room = _roomService.GetRoom(roomId);
        var player = room?.Players.FirstOrDefault(p => p.PlayerId == Context.ConnectionId);

        if (player != null)
        {
            await Clients.Group(roomId).SendAsync("ErrorMessage", $"{player.Name}님이 뻥을 선언했습니다!! ");
            await Clients.Group(roomId).SendAsync("RoomUpdated", room);
        }
    }


    // 가로채기 가능 여부 판단 보조 메서드 (Hub 내부에 작성하거나 Service로 이동 가능)
    private bool CheckCanIntercept(Player player, PlayingCard playedCard)
    {
        // 3.1 룰: 내가 2장(같은 숫자) 들고 있는데 남이 그 숫자를 냈을 때
        if (player.Hand.Count == 2)
        {
            bool isPair = player.Hand[0].Rank == player.Hand[1].Rank || player.Hand.Any(c => c.Rank == "Joker");
            bool matchesPlayed = player.Hand.Any(c => c.Rank == playedCard.Rank) || playedCard.Rank == "Joker";
            if (isPair && matchesPlayed) return true;
        }

        // 2.1 룰: 내가 5장(3장+2장 구성) 들고 있는데 남이 내 3장짜리와 같은 숫자를 냈을 때
        if (player.Hand.Count == 5)
        {
            var groupCounts = player.Hand.Where(c => c.Rank != "Joker")
                                        .GroupBy(c => c.Rank)
                                        .ToDictionary(g => g.Key, g => g.Count());
            
            int jokers = player.Hand.Count(c => c.Rank == "Joker");

            // 내가 3장(조커 포함)을 만들 수 있는 랭크들 중 하나가 버려진 카드와 같은지 확인
            foreach (var rank in groupCounts.Keys)
            {
                if (groupCounts[rank] + jokers >= 3 && rank == playedCard.Rank)
                {
                    // 남은 카드들이 2장(또는 조커 사용 후 2장) 세트가 되는지 추가 검증 필요시 CanMakeGroups 호출
                    return true; 
                }
            }
        }

        return false;
    }

    // 바가지 판별
    public async Task InterceptWin(string roomId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room == null || room.LastDiscardedCard == null) return;

        var player = room.Players.FirstOrDefault(p => p.PlayerId == Context.ConnectionId);
        if (player == null) return;

        // 마지막으로 버려진 카드와 내 패를 대조
        if (CheckCanIntercept(player, room.LastDiscardedCard))
        {
            // 카드를 마지막에 버린 사람(방출자) 찾기
            var loser = room.Players.FirstOrDefault(p => p.PlayerId != player.PlayerId && p.PlayerId != room.CurrentTurnPlayerId); 
            // 실제로는 방출자 ID를 Room에 저장해두는 것이 가장 정확합니다.
            
            _roomService.DeclareInterceptionWin(room, player, room.LastActorPlayerId); 
            await Clients.Group(roomId).SendAsync("RoomUpdated", room);
            await Clients.Group(roomId).SendAsync("ShowResultBoard", room);
        }
    }

    public async Task DrawCard(string roomId)
    {
        var updatedRoom = _roomService.DrawCard(roomId, Context.ConnectionId);
        if (updatedRoom != null)
        {
            // 직접 플레이어를 찾아 턴 횟수 증가
            var player = updatedRoom.Players.FirstOrDefault(p => p.PlayerId == Context.ConnectionId);
            if (player != null)
            {
                player.RoundTurnCount++;
            }
            
            await Clients.Group(roomId).SendAsync("RoomUpdated", updatedRoom);
        }
    }

    public async Task ReshuffleDeck(string roomId)
    {
        var playerId = Context.ConnectionId; // 또는 유저 ID
        var success = _roomService.ReshuffleDiscardPile(roomId, playerId);

        if (success)
        {
            // 방 안의 모든 유저에게 덱이 갱신되었음을 알림
            await Clients.Group(roomId).SendAsync("ReshuffleDeck", "버려진 카드가 다시 덱으로 들어갔습니다.");
            
            // 갱신된 방 상태 전송 (덱 개수 등을 클라이언트에서 업데이트하기 위함)
            var room = _roomService.GetRoom(roomId);
            await Clients.Group(roomId).SendAsync("ReshuffleDeck", room);
        }
    }

    // DrawCard(카드 뽑기) 메서드도 미리 추가해두세요 (에러 방지)
    // [Authorize]
    // public async Task DrawCard(string roomId)
    // {
    //     var updatedRoom = _roomService.DrawCard(roomId, Context.ConnectionId);
    //     if (updatedRoom != null)
    //     {
    //         await Clients.Group(roomId).SendAsync("RoomUpdated", updatedRoom);
    //     }
    // }
    
    // 승리 로직 계산
    public async Task DeclareWin(string roomId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room == null) return;

        var player = room.Players
            .FirstOrDefault(p => p.PlayerId == Context.ConnectionId);
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

        await Clients.Group(roomId).SendAsync("RoomUpdated", room);
    }

    public async Task RequestNextRound(string roomId)
    {
        try 
        {
            var room = _roomService.GetRoom(roomId);
            if (room == null) return;

            // 방장만 다음 라운드를 시작할 수 있도록 권한 체크 추가
            if (room.HostPlayerId != Context.ConnectionId)
            {
                await Clients.Caller.SendAsync("ErrorMessage", "방장만 다음 라운드를 시작할 수 있습니다.");
                return;
            }

            // 1. 서비스에서 덱 생성, '셔플', 카드 분배, 턴 설정을 모두 수행
            _roomService.StartNextRound(room);

            // 2. 모든 플레이어에게 전광판을 닫으라고 명령
            await Clients.Group(roomId).SendAsync("HideResultBoard");

            // 3. 갱신된 방 상태(새 패, 새로운 턴 등)를 전송
            // GetRoom에서 사용하는 익명 객체 구조와 동일하게 보내야 클라이언트 UI가 깨지지 않습니다.
            var roomState = await GetRoom(roomId); 
            await Clients.Group(roomId).SendAsync("RoomUpdated", roomState);
            
            Console.WriteLine($"Next Round Started: {room.CurrentRound}. Turn: {room.CurrentTurnPlayerId}");
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync("ErrorMessage", "라운드 전환 중 오류: " + ex.Message);
        }
    }

    private bool CanDeclareWin(GameRoom room, Player player)
    {
        if (room.IsFinished || room.IsRoundEnded)
            return false;

        // 내 턴인지
        if (room.CurrentTurnPlayerId != player.PlayerId)
            return false;

        // ❗ 누군가의 2번째 턴 시작 조건
        return player.RoundTurnCount >= 2;
    }
    
    // 기권
    public async Task GiveUp(string roomId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room == null) return;

        // 1. 기권 처리 (IsFinished = true 처리됨)
        _roomService.GiveUpGame(roomId, Context.ConnectionId);

        // 2. ⭐ 명시적으로 게임 시작 상태를 해제 (대기실 복귀용)
        room.IsStarted = false; 

        // 3. 최신 데이터 전송
        await Clients.Group(roomId).SendAsync("RoomUpdated", room);
        
        // 4. 전광판을 띄우는 대신 바로 나가게 하고 싶다면 이 신호를 보냄
        // 만약 결과 확인 후 나가게 하고 싶다면 "GameTerminated" 신호를 사용
        await Clients.Group(roomId).SendAsync("GameTerminated", roomId);
    }

    // ✅ 채팅
    public async Task SendChatMessage(string message)
    {
        var nickname = Context.User?.Identity?.Name ?? "Unknown";
        await Clients.All.SendAsync("ReceiveMessage", nickname, message);
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
                // MinScore: 첫 게임이면 무조건 설정, 이후엔 더 작을 때만 갱신
                if (user.TotalGames == 1 || p.TotalScore < user.MinScore)
                {
                    user.MinScore = p.TotalScore;
                }
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
            .FirstOrDefault(p => p.PlayerId == Context.ConnectionId);
        if (player == null) return;

        player.AllowNaturalBagaji = !player.AllowNaturalBagaji;

        // 본인 UI만 갱신
        await Clients.Caller.SendAsync(
            "NaturalBagajiToggled",
            player.AllowNaturalBagaji
        );

        // 룸 상태 동기화 필요하면
        // await Clients.Group(roomId).SendAsync("RoomUpdated", room);
    }
}