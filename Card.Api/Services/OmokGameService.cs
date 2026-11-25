using Card.Api.Domain;

namespace Card.Api.Services;

public class OmokPlaceResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public bool GameEnded { get; set; }
    public string? EndReason { get; set; }
}

public class OmokGameService
{
    private readonly GameRoomService _roomService;
    private readonly Random _random = new();

    public OmokGameService(GameRoomService roomService)
    {
        _roomService = roomService;
    }

    /// <summary>
    /// 오목 게임 시작: 흑/백 랜덤 배정, 빈 보드, 흑 선공.
    /// 정확히 2명일 때만 가능.
    /// </summary>
    public bool StartOmokGame(string roomId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room == null) return false;
        if (room.GameMode != GameMode.Omok) return false;
        if (room.IsStarted) return false;
        if (room.Players.Count != 2) return false;

        bool firstIsBlack = _random.Next(2) == 0;
        var p0 = room.Players[0].PlayerId;
        var p1 = room.Players[1].PlayerId;

        room.Omok = new OmokState
        {
            BlackPlayerId = firstIsBlack ? p0 : p1,
            WhitePlayerId = firstIsBlack ? p1 : p0,
            CurrentTurn = StoneColor.Black
        };
        room.IsStarted = true;
        room.IsFinished = false;
        return true;
    }

    /// <summary>돌 놓기. 검증 + 승리/금수/무승부 판정 일체 처리.</summary>
    public OmokPlaceResult PlaceStone(string roomId, string playerId, int row, int col)
    {
        var room = _roomService.GetRoom(roomId);
        if (room == null || room.GameMode != GameMode.Omok || room.Omok == null)
            return Fail("오목 게임 방이 아닙니다.");

        var state = room.Omok;
        if (state.IsFinished) return Fail("이미 종료된 게임입니다.");
        if (row < 0 || row >= OmokState.BoardSize || col < 0 || col >= OmokState.BoardSize)
            return Fail("보드 범위 밖입니다.");
        if (state.Board[row][col] != StoneColor.Empty)
            return Fail("이미 돌이 있는 자리입니다.");

        StoneColor myColor;
        if (state.BlackPlayerId == playerId) myColor = StoneColor.Black;
        else if (state.WhitePlayerId == playerId) myColor = StoneColor.White;
        else return Fail("플레이어가 아닙니다.");

        if (state.CurrentTurn != myColor) return Fail("상대 차례입니다.");

        // 흑은 33 금수 — 사전 검증으로 거부 (돌을 두지 않음)
        // 가상으로 돌을 놓고 33인지 시뮬레이션
        if (myColor == StoneColor.Black)
        {
            state.Board[row][col] = StoneColor.Black;
            // 단, 5목이 동시에 완성되는 경우는 5목 우선 (정통 렌주 룰)
            bool wouldWin = OmokRule.CheckFive(state.Board, row, col, StoneColor.Black) != null;
            bool isForbidden33 = !wouldWin && OmokRule.IsDoubleThree(state.Board, row, col, StoneColor.Black);
            state.Board[row][col] = StoneColor.Empty;
            if (isForbidden33) return Fail("33 금수 — 둘 수 없는 자리입니다.");
        }

        // 돌 놓기 + 수 기록
        state.Board[row][col] = myColor;
        state.Moves.Add(new OmokMove
        {
            Index = state.Moves.Count + 1,
            Row = row,
            Col = col,
            Color = myColor
        });

        // 우선순위: 5목 → (흑만: 44) → 무승부 → 턴 교체. (6목은 더 이상 패배 아님 — 자유롭게 둘 수 있음)
        var fiveLine = OmokRule.CheckFive(state.Board, row, col, myColor);
        if (fiveLine != null)
        {
            EndGame(room, state, myColor, playerId, $"{ColorName(myColor)} 5목 승리", fiveLine);
            return Done(state.EndReason);
        }

        if (myColor == StoneColor.Black)
        {
            if (OmokRule.IsDoubleFour(state.Board, row, col, myColor))
            {
                EndGame(room, state, StoneColor.White, state.WhitePlayerId, "44 금수 (흑 패배)", new List<int[]>());
                return Done(state.EndReason);
            }
        }

        // 무승부 — 보드 가득
        if (state.Moves.Count >= OmokState.BoardSize * OmokState.BoardSize)
        {
            state.IsFinished = true;
            state.IsDraw = true;
            state.EndReason = "무승부 (보드 가득)";
            room.IsFinished = true;
            room.IsStarted = false;
            return Done(state.EndReason);
        }

        // 턴 교체
        state.CurrentTurn = state.CurrentTurn == StoneColor.Black ? StoneColor.White : StoneColor.Black;
        state.TurnStartedAt = DateTime.UtcNow;  // 턴 타임아웃 기준 시각
        return new OmokPlaceResult { Success = true };
    }

    /// <summary>기권. 호출자의 반대 색이 승리. reason은 종료 사유 표시용(기본 "기권", 퇴장 시 "상대 퇴장").</summary>
    public bool ResignOmok(string roomId, string playerId, string reason = "기권")
    {
        var room = _roomService.GetRoom(roomId);
        if (room?.Omok == null) return false;
        var state = room.Omok;
        if (state.IsFinished) return false;

        StoneColor winnerColor;
        string winnerId;
        if (state.BlackPlayerId == playerId) { winnerColor = StoneColor.White; winnerId = state.WhitePlayerId; }
        else if (state.WhitePlayerId == playerId) { winnerColor = StoneColor.Black; winnerId = state.BlackPlayerId; }
        else return false;

        EndGame(room, state, winnerColor, winnerId, reason, new List<int[]>());
        return true;
    }

    private void EndGame(GameRoom room, OmokState state, StoneColor winnerColor, string? winnerId,
        string reason, List<int[]> winningLine)
    {
        state.IsFinished = true;
        state.WinnerColor = winnerColor;
        state.WinnerPlayerId = winnerId;
        state.EndReason = reason;
        state.WinningLine = winningLine;
        room.IsFinished = true;
        room.IsStarted = false;
    }

    /// <summary>현재 차례가 흑이고 게임이 진행 중일 때 흑의 33 금수 자리 목록 (그 외 빈 리스트).</summary>
    public List<int[]> GetCurrentBlackForbiddenPoints(string roomId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room?.Omok == null) return new List<int[]>();
        var state = room.Omok;
        if (state.IsFinished) return new List<int[]>();
        if (state.CurrentTurn != StoneColor.Black) return new List<int[]>();
        return OmokRule.FindForbidden33Points(state.Board, StoneColor.Black);
    }

    private static string ColorName(StoneColor c) => c == StoneColor.Black ? "흑" : "백";
    private static OmokPlaceResult Fail(string msg) => new() { Success = false, Error = msg };
    private static OmokPlaceResult Done(string? reason) => new() { Success = true, GameEnded = true, EndReason = reason };
}
