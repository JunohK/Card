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
    public async Task StartGame(string roomId)
    {
        var room = _roomService.GetRoom(roomId);
        // 방장인지 확인 후 시작
        if (room != null && room.HostPlayerId == Context.ConnectionId)
        {
            _roomService.StartGame(roomId);
            
            // 모든 클라이언트에게 방 상태(isStarted=true) 업데이트
            await Clients.Group(roomId).SendAsync("RoomUpdated", room);
            // 모든 클라이언트에게 게임 화면으로 이동하라는 전용 신호 전송
            await Clients.Group(roomId).SendAsync("GameStarted", room);
        }
    }

    public async Task<object> GetRoom(string roomId)
    {
        var room = _roomService.GetRoom(roomId);
        if(room == null)
        {
            throw new HubException("방을 찾을 수 없습니다.");
        }

        // 클라이언트가 기대하는 GameState 구조로 변환
        return new
        {
            RoomId = room.RoomId,
            Title = room.Title,
            Players = room.Players.Select(p => new
            {
                PlayerId = p.PlayerId,
                Name = p.Name,
                Hand = p.Hand ?? new List<PlayingCard>()
            }),
            CurrentTurnPlayerId = room.CurrentTurnPlayerId,
            LastDiscardedCard = room.LastDiscardedCard,
            DeckCount = room.DeckCount,
            IsStarted = room.IsStarted,
            IsGameOver = room.IsGameOver,
            WinnerName = room.WinnerName,
            HostPlayerId = room.HostPlayerId
        };
    }

    public async Task PlayCard(string roomId, PlayingCard card)
    {
        var updatedRoom = _roomService.PlayCard(roomId, Context.ConnectionId, card);
        if (updatedRoom != null)
        {
            await Clients.Group(roomId).SendAsync("RoomUpdated", updatedRoom);
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

        try 
        {
            // Hub에서 호출할 때는 일반적인 Win 선언으로 처리
            _roomService.DeclareWin(room, player, WinReason.ManualDeclare);
            await Clients.Group(roomId).SendAsync("RoomUpdated", room);
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync("ErrorMessage", ex.Message);
        }
    }
    
    // 기권
    public async Task GiveUp(string roomId)
        {
            _roomService.GiveUpGame(roomId, Context.ConnectionId);
            var room = _roomService.GetRoom(roomId);
            
            // 방에 있는 모든 사람에게 업데이트된 정보(IsFinished=true) 전송
            await Clients.Group(roomId).SendAsync("RoomUpdated", room);
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