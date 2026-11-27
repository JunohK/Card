using Card.Api.Domain;
using Microsoft.AspNetCore.SignalR;

namespace Card.Hubs;

public partial class GameHub
{
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
        await SendOmokResignedAsync(Clients, room);
    }

    // 오목 턴 타임아웃 — 시간 초과한 사람 자동 기권(패배)
    private async Task OmokTurnTimeoutAsync(string roomId, GameRoom room, string timedOutPlayerId)
    {
        var omok = room.Omok;
        if (omok == null || omok.IsFinished) return;
        var currentId = omok.CurrentTurn == StoneColor.Black ? omok.BlackPlayerId : omok.WhitePlayerId;
        if (currentId != timedOutPlayerId) return;
        if ((DateTime.UtcNow - omok.TurnStartedAt).TotalSeconds < room.TurnTimeoutSec - TurnTimeoutGraceSec) return;

        if (!_omokService.ResignOmok(roomId, currentId)) return;
        var r = _roomService.GetRoom(roomId);
        if (r == null) return;
        await BroadcastRoomAsync(r);
        await SendOmokResignedAsync(Clients, r);
    }

    // 오목 종료 알림 — 기권·퇴장·시간 초과 공용 (승리 줄 없음, 무승부 아님)
    private static Task SendOmokResignedAsync(IHubClients<IClientProxy> clients, GameRoom room) =>
        clients.Group(room.RoomId).SendAsync("OmokGameEnded", new
        {
            Reason = room.Omok?.EndReason,
            WinnerPlayerId = room.Omok?.WinnerPlayerId,
            WinnerColor = room.Omok?.WinnerColor,
            IsDraw = false,
            WinningLine = new List<int[]>()
        });
}
