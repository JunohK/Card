using System.Text.Json;
using Card.Api.Domain;
using static Card.Api.Tests.Cards;

namespace Card.Api.Tests;

// CreateViewFor 마스킹 특성화 테스트 — 클라이언트로 나가는 비공개 정보 보호
public class RoomViewMaskingTests
{
    private static GameRoom TwoPlayerRoom()
    {
        var room = new GameRoom { RoomId = "r", Title = "t" };
        room.Players.Add(new Player { PlayerId = "p1", Name = "p1", Hand = Hand("8", "8") });
        room.Players.Add(new Player { PlayerId = "p2", Name = "p2", Hand = Hand("K", "Q", "J") });
        return room;
    }

    [Fact]
    public void Cards_뷰는_상대_손패를_비우고_장수와_바가지여부만_남긴다()
    {
        var room = TwoPlayerRoom();
        var view = room.CreateViewFor("p2");

        var p1 = view.Players.Single(p => p.PlayerId == "p1");
        var p2 = view.Players.Single(p => p.PlayerId == "p2");
        Assert.Empty(p1.Hand);
        Assert.Equal(2, p1.HandCount);
        Assert.True(p1.IsBagaji);
        Assert.Equal(3, p2.Hand.Count);
        Assert.Equal(3, p2.HandCount);
        Assert.False(p2.IsBagaji);
        // 원본은 변경되지 않음
        Assert.Equal(2, room.Players[0].Hand.Count);
    }

    [Fact]
    public void JSON에_Deck과_Password는_나가지_않는다()
    {
        var room = TwoPlayerRoom();
        room.Password = "secret";
        room.Deck = Hand("2", "3", "4");

        var json = JsonSerializer.Serialize(room.CreateViewFor("p1"));

        Assert.DoesNotContain("\"Password\"", json);
        Assert.DoesNotContain("secret", json);
        Assert.DoesNotContain("\"Deck\":", json);
        Assert.Contains("\"DeckCount\":3", json);
        Assert.DoesNotContain("\"PungWindowFlag\"", json);
        Assert.DoesNotContain("\"PungWindowSeq\"", json);
        Assert.DoesNotContain("\"PungWindowDraw\"", json);   // 대기 시간 중 뽑은 카드 기록은 비공개
        Assert.Contains("\"IsPungWindowOpen\":false", json);
    }

    [Fact]
    public void 족보_힌트는_본인_뷰에만_실린다()
    {
        var (svc, room) = Rooms.Cards(2);
        svc.StartGame(room.RoomId, 3);
        Rooms.P(room, "p1").Hand = Hand("5", "6", "7", "8", "9");

        var mine = svc.CreateViewFor(room, "p1");
        var theirs = svc.CreateViewFor(room, "p2");

        var myHint = mine.Players.Single(p => p.PlayerId == "p1").WinHint;
        Assert.NotNull(myHint);
        Assert.Contains(myHint!.Outs, o => o.Rank == "4");
        Assert.Null(theirs.Players.Single(p => p.PlayerId == "p1").WinHint);   // 상대 뷰에서는 내 힌트가 없음
        Assert.Null(room.Players.Single(p => p.PlayerId == "p1").WinHint);     // 원본에는 기록하지 않음
    }

    [Fact]
    public void Bang_뷰는_미공개_역할을_Outlaw로_마스킹하고_종료후_공개()
    {
        var room = TwoPlayerRoom();
        var bang = new BangState();
        bang.Players.Add(new BangPlayer { PlayerId = "p1", Role = BangRole.Sheriff, RoleRevealed = true, Hand = { new BangCard { Id = 1 } } });
        bang.Players.Add(new BangPlayer { PlayerId = "p2", Role = BangRole.Deputy, Hand = { new BangCard { Id = 2 }, new BangCard { Id = 3 } } });
        bang.Players.Add(new BangPlayer { PlayerId = "p3", Role = BangRole.Renegade });
        room.Bang = bang;

        var view = room.CreateViewFor("p2").Bang!;
        Assert.Equal(BangRole.Sheriff, view.Players[0].Role);     // 공개된 역할
        Assert.Equal(BangRole.Deputy, view.Players[1].Role);      // 본인
        Assert.Equal(BangRole.Outlaw, view.Players[2].Role);      // 상대 비공개 → 마스킹
        Assert.Empty(view.Players[0].Hand);
        Assert.Equal(1, view.Players[0].HandCount);
        Assert.Equal(2, view.Players[1].Hand.Count);

        bang.IsFinished = true;
        var finished = room.CreateViewFor("p2").Bang!;
        Assert.Equal(BangRole.Renegade, finished.Players[2].Role);
    }
}
