using Card.Api.Domain;

namespace Card.Api.Services;

/// <summary>
/// 오목 렌주 룰 판정 헬퍼 (15x15).
/// - 5목 검출 (양 색 공통)
/// - 6목 검출 (흑 금수)
/// - 활삼 / 활사 검출 (33, 44 금수 — 단순화된 패턴 매칭)
/// </summary>
public static class OmokRule
{
    public const int BoardSize = OmokState.BoardSize;

    // 4개 축 (각 축은 양방향으로 탐색)
    private static readonly (int dr, int dc)[] Directions =
    {
        (0, 1),   // 가로
        (1, 0),   // 세로
        (1, 1),   // 대각선 ↘
        (1, -1)   // 역대각선 ↙
    };

    private static bool InRange(int r, int c) => r >= 0 && r < BoardSize && c >= 0 && c < BoardSize;

    // (row,col)에 color를 놓은 후 그 점을 포함한 연속 동일 색 좌표 리스트
    private static List<int[]> CountLine(StoneColor[][] board, int row, int col, int dr, int dc, StoneColor color)
    {
        var line = new List<int[]> { new[] { row, col } };
        int r = row + dr, c = col + dc;
        while (InRange(r, c) && board[r][c] == color)
        {
            line.Add(new[] { r, c });
            r += dr; c += dc;
        }
        r = row - dr; c = col - dc;
        while (InRange(r, c) && board[r][c] == color)
        {
            line.Insert(0, new[] { r, c });
            r -= dr; c -= dc;
        }
        return line;
    }

    /// <summary>정확히 5목을 만든 좌표 리스트 반환 (없으면 null).</summary>
    public static List<int[]>? CheckFive(StoneColor[][] board, int row, int col, StoneColor color)
    {
        foreach (var (dr, dc) in Directions)
        {
            var line = CountLine(board, row, col, dr, dc, color);
            if (line.Count == 5) return line;
        }
        return null;
    }

    /// <summary>6목 이상 (흑이면 패배).</summary>
    public static bool IsOverline(StoneColor[][] board, int row, int col, StoneColor color)
    {
        foreach (var (dr, dc) in Directions)
        {
            if (CountLine(board, row, col, dr, dc, color).Count >= 6) return true;
        }
        return false;
    }

    /// <summary>
    /// 보드 전체에서 color가 두면 33이 되는 모든 빈 자리 반환.
    /// 5목 완성 자리는 5목 우선이므로 33으로 표시하지 않음.
    /// </summary>
    public static List<int[]> FindForbidden33Points(StoneColor[][] board, StoneColor color)
    {
        var forbidden = new List<int[]>();
        for (int r = 0; r < BoardSize; r++)
        {
            for (int c = 0; c < BoardSize; c++)
            {
                if (board[r][c] != StoneColor.Empty) continue;
                board[r][c] = color;
                bool wouldWin = CheckFive(board, r, c, color) != null;
                bool is33 = !wouldWin && IsDoubleThree(board, r, c, color);
                board[r][c] = StoneColor.Empty;
                if (is33) forbidden.Add(new[] { r, c });
            }
        }
        return forbidden;
    }

    /// <summary>33 금수 — 활삼(open three) 2개 이상 동시 형성.</summary>
    public static bool IsDoubleThree(StoneColor[][] board, int row, int col, StoneColor color)
    {
        int count = 0;
        foreach (var (dr, dc) in Directions)
        {
            if (FormsOpenThree(board, row, col, dr, dc, color)) count++;
            if (count >= 2) return true;
        }
        return false;
    }

    /// <summary>44 금수 — 사(four) 2개 이상 동시 형성.</summary>
    public static bool IsDoubleFour(StoneColor[][] board, int row, int col, StoneColor color)
    {
        int count = 0;
        foreach (var (dr, dc) in Directions)
        {
            if (FormsFour(board, row, col, dr, dc, color)) count++;
            if (count >= 2) return true;
        }
        return false;
    }

    // 활삼: 연속 3개 + 양쪽 인접이 비어 있고, 한쪽이라도 그 바깥 칸까지 비어 열린 4(.XXXX.)가 될 수 있는 경우
    // (단순화 — 점프형 X X . X 제외. O.XXX.O 처럼 양 바깥이 막혀 열린 4가 불가능하면 활삼이 아님)
    private static bool FormsOpenThree(StoneColor[][] board, int row, int col, int dr, int dc, StoneColor color)
    {
        var line = CountLine(board, row, col, dr, dc, color);
        if (line.Count != 3) return false;

        var first = line[0];
        var last = line[^1];
        int br = first[0] - dr, bc = first[1] - dc;   // 앞쪽 인접 칸
        int ar = last[0] + dr, ac = last[1] + dc;     // 뒤쪽 인접 칸
        if (!IsEmpty(board, br, bc) || !IsEmpty(board, ar, ac)) return false;

        // 어느 한쪽이든 두 칸 바깥이 비어 있어야 열린 4가 가능
        return IsEmpty(board, br - dr, bc - dc) || IsEmpty(board, ar + dr, ac + dc);
    }

    // 사: 연속 4개 중 한쪽이라도 인접 칸이 비어 있어 다음 수에 정확히 5목이 되는 경우
    // (양쪽이 돌/벽으로 막힌 OXXXXO 는 5목이 될 수 없으므로 사가 아님. 빈칸 너머가 같은 색이면 6목이 되므로 제외)
    private static bool FormsFour(StoneColor[][] board, int row, int col, int dr, int dc, StoneColor color)
    {
        var line = CountLine(board, row, col, dr, dc, color);
        if (line.Count != 4) return false;

        var first = line[0];
        var last = line[^1];
        return CanCompleteFive(board, first[0] - dr, first[1] - dc, -dr, -dc, color)
            || CanCompleteFive(board, last[0] + dr, last[1] + dc, dr, dc, color);
    }

    private static bool IsEmpty(StoneColor[][] board, int r, int c) => InRange(r, c) && board[r][c] == StoneColor.Empty;

    // (r,c)가 비어 있고 그 너머(dr,dc 방향) 칸이 같은 색이 아니면, (r,c)에 두어 정확히 5목을 완성할 수 있다
    private static bool CanCompleteFive(StoneColor[][] board, int r, int c, int dr, int dc, StoneColor color)
    {
        if (!IsEmpty(board, r, c)) return false;
        int r2 = r + dr, c2 = c + dc;
        return !(InRange(r2, c2) && board[r2][c2] == color);
    }
}
