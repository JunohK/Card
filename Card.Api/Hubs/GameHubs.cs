using Card.Api.Services;
using Card.Api.Domain;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;

namespace Card.Hubs;

[Authorize]
public class GameHub : Hub
{
    private readonly GameRoomService _roomService;

    public GameHub(GameRoomService roomService)
    {
        _roomService = roomService;
    }

    /// <summary>
    /// 방 생성
    /// </summary>
    public async Task CreateRoom(string playerName)
    {
        var room = _roomService.CreateRoom(playerName);

        await Groups.AddToGroupAsync(Context.ConnectionId, room.RoomId);

        await Clients.Caller.SendAsync("RoomCreated", room.RoomId);
    }

    /// <summary>
    /// 방 입장
    /// </summary>
    public async Task JoinRoom(string roomId, string playerName)
    {
        var room = _roomService.JoinRoom(roomId, playerName);
        if (room == null)
            return;

        await Groups.AddToGroupAsync(Context.ConnectionId, roomId);

        await Clients.Group(roomId).SendAsync("RoomUpdated", room);
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
    public async Task SendChatMessage(string user, string message)
    {
        await Clients.All.SendAsync("ReceiveMessage", user, message);
    }

    // SignalR 인증 연동(JWT)
    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        var nickname = Context.User?.Identity?.Name;

        Console.WriteLine($"Connected : {nickname} ({userId})");

        await base.OnConnectedAsync();
    }

    public async Task SendSystemMessage(string message)
    {
        var nickname = Context.User?.Identity?.Name ?? "Unknown";
        await Clients.All.SendAsync("ReceiveMessage", nickname, message);
    }
}