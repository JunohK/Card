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

    // 활삼: 연속 3개 + 양쪽이 비어있음 (단순화 — 점프형 X X . X 제외)
    private static bool FormsOpenThree(StoneColor[][] board, int row, int col, int dr, int dc, StoneColor color)
    {
        var line = CountLine(board, row, col, dr, dc, color);
        if (line.Count != 3) return false;

        var first = line[0];
        var last = line[^1];
        int br = first[0] - dr, bc = first[1] - dc;
        int ar = last[0] + dr, ac = last[1] + dc;

        return InRange(br, bc) && board[br][bc] == StoneColor.Empty
            && InRange(ar, ac) && board[ar][ac] == StoneColor.Empty;
    }

    // 사: 연속 4개 (한 쪽이라도 비어있으면 다음 수에 5목 가능)
    private static bool FormsFour(StoneColor[][] board, int row, int col, int dr, int dc, StoneColor color)
    {
        var line = CountLine(board, row, col, dr, dc, color);
        return line.Count == 4;
    }
}
