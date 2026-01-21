using Card.Api.Services;
using Card.Api.Domain;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;

namespace Card.Hubs;

[Authorize]
public class GameHub : Hub
{
    private readonly GameRoomService _roomService;
    private readonly PlayerConnectionService _connService;

    public GameHub(GameRoomService roomService, PlayerConnectionService connService)
    {
        _roomService = roomService;
        _connService = connService;
    }

    // ✅ 로비 진입 (닉네임 전송 및 목록 갱신)
    public async Task EnterLobby()
    {
        var nickname = Context.User?.Identity?.Name ?? "Unknown";
        await Clients.Caller.SendAsync("ConnectedUser", nickname);
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

            // 1. 카드 플레이 처리 (턴 교체 포함)
            _roomService.PlayCard(roomId, Context.ConnectionId, card);

            // 2. 가로채기 체크 전, 일단 현재 상태를 모든 인원에게 즉시 전파 (동기화 보장)
            // 이걸 먼저 보내야 방장이 아닌 사람들도 "누가 카드를 냈다"는걸 압니다.
            await Clients.Group(roomId).SendAsync("RoomUpdated", room);

            // 3. 가로채기(Interception) 체크 로직
            var interceptor = room.Players.FirstOrDefault(p => 
                p.PlayerId != Context.ConnectionId && CheckCanIntercept(p, card));

            if (interceptor != null)
            {
                _roomService.DeclareInterceptionWin(room, interceptor, room.LastActorPlayerId);
                
                // 결과판은 데이터가 완전히 바뀐 후 전송
                await Clients.Group(roomId).SendAsync("ShowResultBoard", room);
                await Clients.Group(roomId).SendAsync("RoomUpdated", room);
            }
            else
            {
                // 가로채기가 없을 때의 부가 효과 처리
                var nextPlayer = room.Players.FirstOrDefault(p => p.PlayerId == room.CurrentTurnPlayerId);
                if (nextPlayer != null && nextPlayer.Hand.Count == 2)
                {
                    if (nextPlayer.Hand[0].Rank == nextPlayer.Hand[1].Rank || nextPlayer.Hand.Any(c => c.Rank == "Joker"))
                    {
                        await Clients.Group(roomId).SendAsync("ShowWaitingMark", nextPlayer.PlayerId);
                    }
                }
                // 마지막으로 다시 한 번 동기화 (턴이 넘어갔음을 알림)
                await Clients.Group(roomId).SendAsync("RoomUpdated", room);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"PlayCard Error: {ex.Message}");
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
            await Clients.Group(roomId).SendAsync("DeckReshuffled", "버려진 카드가 다시 덱으로 들어갔습니다.");
            
            // 갱신된 방 상태 전송 (덱 개수 등을 클라이언트에서 업데이트하기 위함)
            var room = _roomService.GetRoom(roomId);
            await Clients.Group(roomId).SendAsync("UpdateRoom", room);
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

        var player = room.Players.FirstOrDefault(p => p.PlayerId == Context.ConnectionId);
        if (player == null) return;

        bool success = _roomService.DeclareWin(room, player, WinReason.ManualDeclare);

        if (success)
        {
            // 모든 인원에게 라운드 결과(전광판) 동기화
            await Clients.Group(roomId).SendAsync("RoomUpdated", room);
            await Clients.Group(roomId).SendAsync("ShowResultBoard", room);
        }
        else
        {
            var check = _roomService.CheckWinCondition(player.Hand);
            await Clients.Caller.SendAsync("ErrorMessage", $"족보가 맞지 않습니다: {check.winType}");
        }
    }

    // 다음 라운드 시작 요청 처리
    public async Task RequestNextRound(string roomId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room == null) return;

        // 서버 데이터 갱신
        _roomService.StartNextRound(room);

        // 모든 플레이어의 화면을 새 게임 상태로 전환
        await Clients.Group(roomId).SendAsync("RoomUpdated", room);
        await Clients.Group(roomId).SendAsync("HideResultBoard"); // 전광판 닫기
    }

    public async Task GoToNextRound(string roomId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room == null || room.HostPlayerId != Context.ConnectionId) return;

        if (!room.IsFinished && room.IsRoundEnded)
        {
            // 다음 라운드 번호 증가 및 카드 재분배
            room.CurrentRound++;
            // 서비스에 SetupRound를 public으로 하거나, 아래처럼 별도 처리 메서드 호출
            _roomService.StartGame(roomId, room.MaxRounds); // 재시작 로직 활용
            
            await Clients.Group(roomId).SendAsync("GameStarted", room);
        }
    }
    
    // 기권
    public async Task GiveUp(string roomId)
    {
        // 1. 일단 서비스 로직을 실행해서 DB(메모리) 값을 먼저 바꿉니다.
        var room = _roomService.GetRoom(roomId);
        if (room == null) return;

        if (room.IsFinished) 
        {
            _roomService.CompleteGame(roomId); // 내부에서 IsStarted = false 처리 완료
        }
        else 
        {
            _roomService.GiveUpGame(roomId, Context.ConnectionId); // 기권 처리
        }

        // 2. 🔴 중요: 상태가 변경된 '최신' 객체를 다시 가져옵니다.
        var updatedRoom = _roomService.GetRoom(roomId);

        // 3. 최신 데이터를 전송합니다. (이제 IsStarted가 false인 것이 보장됨)
        await Clients.Group(roomId).SendAsync("RoomUpdated", updatedRoom);
        
        // 4. 안전장치: 아예 명시적 신호를 하나 더 보냅니다.
        if (updatedRoom != null && !updatedRoom.IsStarted)
        {
            await Clients.Group(roomId).SendAsync("ExitToRoom", roomId);
        }
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
}