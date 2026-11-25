using Card.Api.Domain;
using Card.Api.Services;

namespace Card.Api.Tests;

public class OmokRuleTests
{
    private static StoneColor[][] Board() => OmokState.CreateEmptyBoard();
    private static void Put(StoneColor[][] b, StoneColor color, params (int r, int c)[] pts)
    {
        foreach (var (r, c) in pts) b[r][c] = color;
    }

    [Fact]
    public void CheckFive_정확히_5개만()
    {
        var b = Board();
        Put(b, StoneColor.Black, (7, 3), (7, 4), (7, 5), (7, 6));
        Assert.Null(OmokRule.CheckFive(b, 7, 5, StoneColor.Black));       // 4개
        Put(b, StoneColor.Black, (7, 7));
        var line = OmokRule.CheckFive(b, 7, 5, StoneColor.Black);
        Assert.NotNull(line);
        Assert.Equal(5, line!.Count);
        Put(b, StoneColor.Black, (7, 8));
        Assert.Null(OmokRule.CheckFive(b, 7, 5, StoneColor.Black));       // 6개는 5목 아님
        Assert.True(OmokRule.IsOverline(b, 7, 5, StoneColor.Black));
    }

    [Fact]
    public void CheckFive_대각선()
    {
        var b = Board();
        Put(b, StoneColor.White, (3, 3), (4, 4), (5, 5), (6, 6), (7, 7));
        Assert.NotNull(OmokRule.CheckFive(b, 5, 5, StoneColor.White));
        Assert.Null(OmokRule.CheckFive(b, 5, 5, StoneColor.Black));
    }

    [Fact]
    public void IsDoubleThree_열린삼_둘()
    {
        var b = Board();
        Put(b, StoneColor.Black, (7, 5), (7, 6), (5, 7), (6, 7), (7, 7));
        Assert.True(OmokRule.IsDoubleThree(b, 7, 7, StoneColor.Black));
    }

    [Fact]
    public void IsDoubleThree_한쪽이_막힌_삼은_세지_않음()
    {
        var b = Board();
        Put(b, StoneColor.Black, (7, 5), (7, 6), (5, 7), (6, 7), (7, 7));
        Put(b, StoneColor.White, (7, 4));                                   // 가로 삼 한쪽 막힘
        Assert.False(OmokRule.IsDoubleThree(b, 7, 7, StoneColor.Black));
    }

    [Fact]
    public void IsDoubleFour_사_둘()
    {
        var b = Board();
        Put(b, StoneColor.Black, (7, 4), (7, 5), (7, 6), (4, 7), (5, 7), (6, 7), (7, 7));
        Assert.True(OmokRule.IsDoubleFour(b, 7, 7, StoneColor.Black));
        Assert.False(OmokRule.IsDoubleThree(b, 7, 7, StoneColor.Black));
    }

    [Fact]
    public void FindForbidden33Points_33자리_포함()
    {
        var b = Board();
        Put(b, StoneColor.Black, (7, 5), (7, 6), (5, 7), (6, 7));
        var pts = OmokRule.FindForbidden33Points(b, StoneColor.Black);
        Assert.Contains(pts, p => p[0] == 7 && p[1] == 7);
        Assert.Equal(StoneColor.Empty, b[7][7]);                            // 보드 원상복구
    }

    [Fact]
    public void FindForbidden33Points_5목완성자리는_금수아님()
    {
        var b = Board();
        Put(b, StoneColor.Black, (7, 3), (7, 4), (7, 5), (7, 6));            // 가로 4 → (7,7)은 5목
        Put(b, StoneColor.Black, (5, 7), (6, 7), (5, 5), (6, 6));            // 세로삼 + 대각삼 재료
        var pts = OmokRule.FindForbidden33Points(b, StoneColor.Black);
        Assert.DoesNotContain(pts, p => p[0] == 7 && p[1] == 7);
    }

    [Fact]
    public void 활삼_두칸_바깥이_양쪽_다_막히면_활삼_아님()        // O.XXX.O
    {
        var b = Board();
        Put(b, StoneColor.Black, (7, 5), (7, 6), (5, 7), (6, 7), (7, 7));   // 가로삼 + 세로삼
        Put(b, StoneColor.White, (7, 3), (7, 9));                          // 가로삼 두 칸 바깥 양쪽 막음
        Assert.False(OmokRule.IsDoubleThree(b, 7, 7, StoneColor.Black));
        b[7][9] = StoneColor.Empty;                                        // 한쪽만 막히면 여전히 활삼
        Assert.True(OmokRule.IsDoubleThree(b, 7, 7, StoneColor.Black));
    }

    [Fact]
    public void 양끝이_막힌_4는_사가_아님()                           // OXXXXO
    {
        var b = Board();
        Put(b, StoneColor.Black, (7, 4), (7, 5), (7, 6), (4, 7), (5, 7), (6, 7), (7, 7));
        Put(b, StoneColor.White, (7, 3), (7, 8));                          // 가로 4 양끝 막음
        Assert.False(OmokRule.IsDoubleFour(b, 7, 7, StoneColor.Black));
        b[7][8] = StoneColor.Empty;                                        // 한쪽 열리면 사
        Assert.True(OmokRule.IsDoubleFour(b, 7, 7, StoneColor.Black));
    }

    [Fact]
    public void 빈칸_너머가_같은색이면_6목이라_사가_아님()             // OXXXX.X
    {
        var b = Board();
        Put(b, StoneColor.Black, (7, 4), (7, 5), (7, 6), (7, 7), (7, 9), (4, 7), (5, 7), (6, 7));
        Put(b, StoneColor.White, (7, 3));
        // 가로: 왼쪽 막힘, 오른쪽 (7,8)에 두면 6목 → 사 아님. 세로만 사이므로 44 아님
        Assert.False(OmokRule.IsDoubleFour(b, 7, 7, StoneColor.Black));
    }
}

public class OmokServiceTests
{
    private static (GameRoomService rs, OmokGameService os, GameRoom room) Setup(int players = 2, GameMode mode = GameMode.Omok)
    {
        var rs = new GameRoomService();
        var room = rs.CreateRoom("p1", "t", null, mode);
        for (int i = 1; i <= players; i++) rs.JoinRoom(room.RoomId, $"p{i}", $"p{i}");
        return (rs, new OmokGameService(rs), room);
    }

    private static (string black, string white) Colors(GameRoom room) =>
        (room.Omok!.BlackPlayerId, room.Omok.WhitePlayerId);

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void Start_정확히_2명일때만(int players, bool expected)
    {
        var (_, os, room) = Setup(players);
        Assert.Equal(expected, os.StartOmokGame(room.RoomId));
        Assert.Equal(expected, room.IsStarted);
    }

    [Fact]
    public void Start_오목모드가_아니거나_이미_시작이면_불가()
    {
        var (_, os, cardsRoom) = Setup(2, GameMode.Cards);
        Assert.False(os.StartOmokGame(cardsRoom.RoomId));

        var (_, os2, room) = Setup(2);
        Assert.True(os2.StartOmokGame(room.RoomId));
        Assert.False(os2.StartOmokGame(room.RoomId));
    }

    [Fact]
    public void Start_흑백은_두_플레이어에게_배정_흑선()
    {
        var (_, os, room) = Setup();
        os.StartOmokGame(room.RoomId);
        var (black, white) = Colors(room);
        Assert.NotEqual(black, white);
        Assert.Contains(black, new[] { "p1", "p2" });
        Assert.Contains(white, new[] { "p1", "p2" });
        Assert.Equal(StoneColor.Black, room.Omok!.CurrentTurn);
        Assert.Empty(room.Omok.Moves);
    }

    [Fact]
    public void PlaceStone_차례_범위_중복_검증()
    {
        var (_, os, room) = Setup();
        os.StartOmokGame(room.RoomId);
        var (black, white) = Colors(room);

        Assert.Equal("상대 차례입니다.", os.PlaceStone(room.RoomId, white, 7, 7).Error);
        Assert.Equal("보드 범위 밖입니다.", os.PlaceStone(room.RoomId, black, 15, 0).Error);
        Assert.Equal("플레이어가 아닙니다.", os.PlaceStone(room.RoomId, "ghost", 7, 7).Error);

        var ok = os.PlaceStone(room.RoomId, black, 7, 7);
        Assert.True(ok.Success);
        Assert.False(ok.GameEnded);
        Assert.Equal(StoneColor.White, room.Omok!.CurrentTurn);
        Assert.Single(room.Omok.Moves);
        Assert.Equal(1, room.Omok.Moves[0].Index);

        Assert.Equal("이미 돌이 있는 자리입니다.", os.PlaceStone(room.RoomId, white, 7, 7).Error);
    }

    [Fact]
    public void PlaceStone_흑_33은_거부되고_돌이_놓이지_않는다()
    {
        var (_, os, room) = Setup();
        os.StartOmokGame(room.RoomId);
        var (black, _) = Colors(room);
        var b = room.Omok!.Board;
        b[7][5] = b[7][6] = b[5][7] = b[6][7] = StoneColor.Black;

        var r = os.PlaceStone(room.RoomId, black, 7, 7);

        Assert.False(r.Success);
        Assert.Contains("33 금수", r.Error);
        Assert.Equal(StoneColor.Empty, b[7][7]);
        Assert.Equal(StoneColor.Black, room.Omok.CurrentTurn);
    }

    [Fact]
    public void PlaceStone_5목이면_종료_승자기록()
    {
        var (_, os, room) = Setup();
        os.StartOmokGame(room.RoomId);
        var (black, _) = Colors(room);
        var b = room.Omok!.Board;
        b[7][3] = b[7][4] = b[7][5] = b[7][6] = StoneColor.Black;

        var r = os.PlaceStone(room.RoomId, black, 7, 7);

        Assert.True(r.GameEnded);
        Assert.Equal("흑 5목 승리", r.EndReason);
        Assert.Equal(black, room.Omok.WinnerPlayerId);
        Assert.Equal(StoneColor.Black, room.Omok.WinnerColor);
        Assert.Equal(5, room.Omok.WinningLine.Count);
        Assert.True(room.IsFinished);
        Assert.False(room.IsStarted);
        Assert.Equal("이미 종료된 게임입니다.", os.PlaceStone(room.RoomId, black, 0, 0).Error);
    }

    [Fact]
    public void PlaceStone_흑_44는_백승리()
    {
        var (_, os, room) = Setup();
        os.StartOmokGame(room.RoomId);
        var (black, white) = Colors(room);
        var b = room.Omok!.Board;
        b[7][4] = b[7][5] = b[7][6] = b[4][7] = b[5][7] = b[6][7] = StoneColor.Black;

        var r = os.PlaceStone(room.RoomId, black, 7, 7);

        Assert.True(r.GameEnded);
        Assert.Equal("44 금수 (흑 패배)", r.EndReason);
        Assert.Equal(white, room.Omok.WinnerPlayerId);
        Assert.Equal(StoneColor.White, room.Omok.WinnerColor);
    }

    [Fact]
    public void Resign_상대가_승리_중복기권_불가()
    {
        var (_, os, room) = Setup();
        os.StartOmokGame(room.RoomId);
        var (black, white) = Colors(room);
        Assert.False(os.ResignOmok(room.RoomId, "ghost"));
        Assert.True(os.ResignOmok(room.RoomId, black));
        Assert.Equal(white, room.Omok!.WinnerPlayerId);
        Assert.Equal("기권", room.Omok.EndReason);
        Assert.False(os.ResignOmok(room.RoomId, white));
    }

    [Fact]
    public void Resign_사유를_지정하면_그대로_기록()
    {
        var (_, os, room) = Setup();
        os.StartOmokGame(room.RoomId);
        var (black, white) = Colors(room);
        Assert.True(os.ResignOmok(room.RoomId, white, "상대 퇴장"));
        Assert.Equal("상대 퇴장", room.Omok!.EndReason);
        Assert.Equal(black, room.Omok.WinnerPlayerId);
    }

    [Fact]
    public void 금수자리는_흑차례에만_계산()
    {
        var (_, os, room) = Setup();
        os.StartOmokGame(room.RoomId);
        var (black, _) = Colors(room);
        var b = room.Omok!.Board;
        b[7][5] = b[7][6] = b[5][7] = b[6][7] = StoneColor.Black;

        Assert.Contains(os.GetCurrentBlackForbiddenPoints(room.RoomId), p => p[0] == 7 && p[1] == 7);
        os.PlaceStone(room.RoomId, black, 0, 0);              // 백 차례로
        Assert.Empty(os.GetCurrentBlackForbiddenPoints(room.RoomId));
    }
}
