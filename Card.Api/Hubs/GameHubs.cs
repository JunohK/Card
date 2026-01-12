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

    public GameHub(
        GameRoomService roomService,
        PlayerConnectionService connService)
    {
        _roomService = roomService;
        _connService = connService;
    }

    public override async Task OnConnectedAsync()
    {
        var nickname = Context.User?.Identity?.Name ?? "Unknown";
        var userId = Context.UserIdentifier;

        Console.WriteLine($"Connected : {nickname} ({userId})");

        Console.WriteLine("SignalR Connected");
        Console.WriteLine("User null? " + (Context.User == null));
        Console.WriteLine("Identity null? " + (Context.User?.Identity == null));
        Console.WriteLine("Name: " + Context.User?.Identity?.Name);

        // ConnectionId <-> Player 바인딩
        _connService.Bind(Context.ConnectionId, nickname);

        // 로그인 시 로비와 방에 아이디 정보 보내기
        await Clients.Caller.SendAsync("ConnectedUser", nickname);

        // 로비 접속 시 방 목록 전달
        await Clients.Caller.SendAsync(
            "RoomList",
            _roomService.GetRooms()
                .Select(r => new RoomSummaryDTO
                {
                    RoomId = r.RoomId,
                    Title = r.Title,
                    PlayerCount = r.Players.Count,
                    IsStarted = r.IsStarted,
                    IsLocked = !string.IsNullOrEmpty(r.Password)
                })
        );

        // await Clients.Caller.SendAsync("RoomList", rooms);

        await base.OnConnectedAsync();
        // await Clients.Caller.SendAsync(
        //     "RoomList",
        //     _roomService.GetRooms()
        // );
    }

    // 새로 고침 / 강제종료 대응
    // public override Task OnDisconnectedAsync(Exception? exception)
    // {
    //     _connService.Unbind(Context.ConnectionId);
    //     return base.OnDisconnectedAsync(exception);
    // }
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        foreach(var room in _roomService.GetRooms())
        {
            var player = room.Players
                .FirstOrDefault(p => p.PlayerId == Context.ConnectionId);

            if(player != null)
            {
                room.Players.Remove(player);
            }
        }

        _connService.Unbind(Context.ConnectionId);

        await Clients.All.SendAsync(
            "RoomList",
            _roomService.GetRooms().Select(r => new RoomSummaryDTO
            {
                RoomId = r.RoomId,
                Title = r.Title,
                PlayerCount = r.Players.Count,
                IsStarted = r.IsStarted,
                IsLocked = !string.IsNullOrEmpty(r.Password),
                PlayerNames = r.Players.Select(p => p.Name).ToList()
            })
        );

        // 로비 닉네임 표시
        await Clients.Caller.SendAsync(
            "ConnectedUser",
            Context.User?.Identity?.Name ?? "Unknown"
        );

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// 로비에 아이디 표시 --> OnConnectedAsync에서 실행 ( 추후 삭제 )
    /// </summary>
    // public Task RequestMyInfo()
    // {
    //     Console.WriteLine("==== RequestMyInfo =====");
    //     Console.WriteLine("IsAuthenticated = " + Context.User?.Identity?.IsAuthenticated);
    //     Console.WriteLine("Name = " + Context.User?.Identity?.Name);

    //     var name = Context.User?.Identity?.Name ?? "Unknown";
    //     return Clients.Caller.SendAsync("ConnectedUser", name);
    // }

    /// <summary>
    /// 방 생성
    /// </summary>
    public async Task CreateRoom(string title, string? password)
    {
        try
        {
            Console.WriteLine("CreateRoom called");

            var nickname = Context.User!.Identity!.Name!;
            Console.WriteLine("nickname: " + nickname);

            var room = _roomService.CreateRoom(nickname, title, password);
            Console.WriteLine("room created: " + room.RoomId);

            await Groups.AddToGroupAsync(Context.ConnectionId, room.RoomId);
            Console.WriteLine("added to group");

            await Clients.All.SendAsync(
                "RoomList",
                _roomService.GetRooms().Select(r => new RoomSummaryDTO
                {
                    RoomId = r.RoomId,
                    Title = r.Title,
                    PlayerCount = r.Players.Count,
                    IsStarted = r.IsStarted,
                    IsLocked = !string.IsNullOrEmpty(r.Password),
                    PlayerNames = r.Players.Select(p => p.Name).ToList()
                })
            );

            await Clients.Caller.SendAsync("RoomCreated", room.RoomId);
        }
        catch (Exception ex)
        {
            Console.WriteLine("🔥 CreateRoom ERROR");
            Console.WriteLine(ex.ToString());
            throw; // ← 이거 있어야 클라이언트에 에러 전달됨
        }
    }


    /// <summary>
    /// 방 입장
    /// </summary>
    public async Task JoinRoom(string roomId, string? password)
    {
        var room = _roomService.GetRoom(roomId);

        if (room == null)
            throw new HubException("방이 존재하지 않습니다.");

        if (!string.IsNullOrEmpty(room.Password) && room.Password != password)
            throw new HubException("비밀번호가 틀렸습니다.");

        var nickname = Context.User!.Identity!.Name;

        if(!room.Players.Any(p => p.PlayerId == Context.ConnectionId))
        {
            room.Players.Add(new Player
            {
                PlayerId = Context.ConnectionId,
                Name = nickname
            });
        }

        // player 객체 추가
        // room.Players.Add(new Player
        // {
        //     PlayerId = Context.ConnectionId,
        //     Name = Context.User?.Identity?.Name ?? "Unknown"
        // });

        await Groups.AddToGroupAsync(Context.ConnectionId, roomId);

        await Clients.Caller.SendAsync("JoinRoomSuccess", roomId);

        await Clients.All.SendAsync(
            "RoomList",
            _roomService.GetRooms().Select(r => new RoomSummaryDTO
            {
                RoomId = r.RoomId,
                Title = r.Title,
                PlayerCount = r.Players.Count,
                IsStarted = r.IsStarted,
                IsLocked = !string.IsNullOrEmpty(r.Password),
                PlayerNames = r.Players.Select(p => p.Name).ToList()
            })
        );

        await Clients.Group(roomId).SendAsync("RoomUpdated", room);
    }


    /// <summary>
    /// 방 나가기
    /// </summary>
    public async Task LeaveRoom(string roomId)
    {
        var room = _roomService.GetRoom(roomId);
        if(room == null)
            return;

        var player = room.Players
            .FirstOrDefault(p => p.PlayerId == Context.ConnectionId);

        if(player != null)
            room.Players.Remove(player);

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomId);

        await Clients.All.SendAsync(
            "RoomList",
            _roomService.GetRooms().Select(r => new RoomSummaryDTO
            {
                RoomId = r.RoomId,
                PlayerCount = r.Players.Count,
                IsStarted = r.IsStarted,
                IsLocked = !string.IsNullOrEmpty(r.Password),
                PlayerNames = r.Players.Select(p => p.Name).ToList()
            })
        );
    }

    /// <summary>
    /// 로비 입장 시 방 목록 불러오기
    /// </summary>
    public async Task RequestRoomList()
    {
        await Clients.Caller.SendAsync(
            "RoomList",
            _roomService.GetRooms().Select(r => new RoomSummaryDTO
            {
                RoomId = r.RoomId,
                Title = r.Title,
                PlayerCount = r.Players.Count,
                IsStarted = r.IsStarted,
                IsLocked = !string.IsNullOrEmpty(r.Password),
                PlayerNames = r.Players.Select(p => p.Name).ToList()
            })
        );
    }

    /// <summary>
    /// 로비 입장 시 새로고침
    /// </summary>
    public async Task EnterLobby()
    {
        var name = Context.User?.Identity?.Name ?? "Unknown";

        // 내 정보
        await Clients.Caller.SendAsync("ConnectedUser", name);

        // 방 목록
        await Clients.Caller.SendAsync(
            "RoomList",
            _roomService.GetRooms().Select(r => new RoomSummaryDTO
            {
                RoomId = r.RoomId,
                Title = r.Title,
                PlayerCount = r.Players.Count,
                IsStarted = r.IsStarted,
                IsLocked = !string.IsNullOrEmpty(r.Password),
                PlayerNames = r.Players.Select(p => p.Name).ToList()
            })
        );
    }

    /// <summary>
    /// 게임 시작 (호스트만)
    /// </summary>
    public async Task StartGame(
        string roomId, 
        int totalRounds // 1,5,10 라운드
        )
    {
        var room = _roomService.GetRoom(roomId);
        if (room == null || room.IsStarted)
            return;

        room.TotalRounds = totalRounds;

        _roomService.StartGame(roomId);

        await Clients.Group(roomId).SendAsync("GameStarted", room);
    }

    /// <summary>
    /// 내 턴 행동
    /// </summary>
    public async Task ActingMyTurn(
        string roomId,
        string playerId,
        TurnActionType actionType,
        List<int>? discardIndexes = null)
    {
        var room = _roomService.GetRoom(roomId);
        if (room == null || room.IsFinished)
            return;

        if (room.CurrentTurnPlayerId != playerId)
            return;

        _roomService.ActingMyTurn(room, playerId, actionType, discardIndexes);

        if (room.IsFinished)
        {
            // 현재 라운드 종료 알림
            await Clients.Group(roomId)
                .SendAsync("RoundFinished", room);

            // 다음 라운드 가능 여부
            if (_roomService.CanStartNextRound(room))
            {
                _roomService.StartNextRound(room);

                await Clients.Group(roomId)
                    .SendAsync("NextRoundStarted", room);
            } 
            else
            {
                // 매치 최종 승자 계산
                var finalWinner = room.Players
                    .OrderByDescending(p => p.Score)
                    .First();

                await Clients.Group(roomId)
                    .SendAsync("MatchFinished",finalWinner);
            }

            return;
        }

        // 턴 정상 진행 알림
        await Clients.Group(roomId)
            .SendAsync("RoomUpdated", room);

        // 게임 종료 여부 체크
        if (room.IsFinished)
        {
            await Clients.Group(roomId).SendAsync(
                "GameFinished",
                room.WinnerPlayerId
            );
            return;
        }

        // 턴 변경 알림
        await Clients.Group(roomId).SendAsync("RoomUpdated", room);
    }

    /// <summary>
    /// 상대 턴 인터럽트 행동
    /// </summary>
    public async Task InterruptAction(
        string roomId,
        string playerId,
        List<int> handIndexes)
    {
        var room = _roomService.GetRoom(roomId);
        if (room == null || room.IsFinished)
            return;

        var success = _roomService.TryInterrupt(
            room,
            playerId,
            handIndexes
        );

        if (!success)
            return;

        if (room.IsFinished)
        {
            await Clients.Group(roomId)
                .SendAsync("RoundFinished", room);

            if (_roomService.CanStartNextRound(room))
            {
                _roomService.StartNextRound(room);

                await Clients.Group(roomId)
                    .SendAsync("NextRoundStarted", room);
            }
            else
            {
                var finalWinner = room.Players
                    .OrderByDescending(p => p.Score)
                    .First();

                await Clients.Group(roomId)
                    .SendAsync("MatchFinished", finalWinner);
            }

            return;
        }

        await Clients.Group(roomId)
            .SendAsync("RoomUpdated", room);
    }

    // 메세지(채팅) 전송 기능 - 클라이언트에서 메세지 보내면 모든 클라이언트에게 전송
    public async Task SendChatMessage(string message)
    {
        var nickname = Context.User?.Identity?.Name ?? "Unknown";
        await Clients.All.SendAsync("ReceiveMessage", nickname, message);
    }

    // SignalR 인증 연동(JWT)
    // public override async Task OnConnectedAsync()
    // {
    //     var userId = Context.UserIdentifier;
    //     var nickname = Context.User?.Identity?.Name;

    //     Console.WriteLine($"Connected : {nickname} ({userId})");

    //     await base.OnConnectedAsync();
    // }

    public async Task SendSystemMessage(string message)
    {
        var nickname = Context.User?.Identity?.Name ?? "Unknown";
        await Clients.All.SendAsync("ReceiveMessage", nickname, message);
    }
}