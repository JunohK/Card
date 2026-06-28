namespace Card.Api.Domain;

public enum StoneColor
{
    Empty = 0,
    Black = 1,
    White = 2
}

public class OmokMove
{
    public int Index { get; set; }      // 1부터 시작하는 수 번호 (1, 2, 3, ...)
    public int Row { get; set; }        // 0-based
    public int Col { get; set; }        // 0-based
    public StoneColor Color { get; set; }
}

public class OmokState
{
    public const int BoardSize = 15;

    // Board[row][col] = StoneColor (Empty/Black/White)
    // 직렬화 시 2차원 배열로 클라이언트에 전송
    public StoneColor[][] Board { get; set; } = CreateEmptyBoard();

    public string BlackPlayerId { get; set; } = string.Empty;
    public string WhitePlayerId { get; set; } = string.Empty;

    public StoneColor CurrentTurn { get; set; } = StoneColor.Black;

    public List<OmokMove> Moves { get; set; } = new();

    // 종료 상태
    public bool IsFinished { get; set; }
    public StoneColor WinnerColor { get; set; } = StoneColor.Empty;  // Empty = 아직/무승부
    public string? WinnerPlayerId { get; set; }
    public bool IsDraw { get; set; }
    public string? EndReason { get; set; }  // "5목 승리", "기권", "무승부", "33 금수 패배" 등

    // 승리한 돌 5개 좌표 (UI 강조용)
    public List<int[]> WinningLine { get; set; } = new();

    // 현재 턴 시작 시각(UTC) — 턴 타임아웃 서버 검증용.
    public DateTime TurnStartedAt { get; set; } = DateTime.UtcNow;

    public static StoneColor[][] CreateEmptyBoard()
    {
        var board = new StoneColor[BoardSize][];
        for (int i = 0; i < BoardSize; i++)
            board[i] = new StoneColor[BoardSize];
        return board;
    }
}
