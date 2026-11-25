using Card.Api.Domain;
using Card.Api.Services;

namespace Card.Api.Tests;

// BangGameService.StartBangGame 특성화 테스트 — 역할 분포, 체력, 카드 보존 불변식
public class BangStartTests
{
    private static (GameRoomService rs, BangGameService bs, GameRoom room) Setup(int players, bool dodgeCity = false)
    {
        var rs = new GameRoomService();
        var room = rs.CreateRoom("p1", "t", null, GameMode.Bang);
        rs.JoinRoom(room.RoomId, "p1", "p1");
        for (int i = 2; i <= players; i++) rs.AddBot(room.RoomId);
        room.BangUseDodgeCity = dodgeCity;
        return (rs, new BangGameService(rs), room);
    }

    [Theory]
    [InlineData(4, 1, 0, 2, 1)]
    [InlineData(5, 1, 1, 2, 1)]
    [InlineData(6, 1, 1, 3, 1)]
    [InlineData(7, 1, 2, 3, 1)]
    public void 역할분포는_인원수_표를_따른다(int players, int sheriff, int deputy, int outlaw, int renegade)
    {
        var (_, bs, room) = Setup(players);
        Assert.True(bs.StartBangGame(room.RoomId));
        var roles = room.Bang!.Players.GroupBy(p => p.Role).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(sheriff, roles.GetValueOrDefault(BangRole.Sheriff));
        Assert.Equal(deputy, roles.GetValueOrDefault(BangRole.Deputy));
        Assert.Equal(outlaw, roles.GetValueOrDefault(BangRole.Outlaw));
        Assert.Equal(renegade, roles.GetValueOrDefault(BangRole.Renegade));
    }

    [Fact]
    public void 인원이_4명_미만이거나_7명_초과면_시작불가()
    {
        var (_, bs3, room3) = Setup(3);
        Assert.False(bs3.StartBangGame(room3.RoomId));
        Assert.False(room3.IsStarted);

        var (_, bs8, room8) = Setup(7);
        room8.Players.Add(new Player { PlayerId = "x", Name = "x" });   // AddBot은 7명에서 막히므로 직접 추가
        Assert.False(bs8.StartBangGame(room8.RoomId));
    }

    [Fact]
    public void 뱅모드_아니거나_이미시작이면_불가()
    {
        var rs = new GameRoomService();
        var cards = rs.CreateRoom("p1", "t", null, GameMode.Cards);
        for (int i = 1; i <= 4; i++) rs.JoinRoom(cards.RoomId, $"p{i}", $"p{i}");
        Assert.False(new BangGameService(rs).StartBangGame(cards.RoomId));

        var (_, bs, room) = Setup(4);
        Assert.True(bs.StartBangGame(room.RoomId));
        Assert.False(bs.StartBangGame(room.RoomId));
    }

    [Fact]
    public void 보안관은_공개_체력보너스_첫턴()
    {
        var (_, bs, room) = Setup(5);
        bs.StartBangGame(room.RoomId);
        var bang = room.Bang!;
        var sheriff = Assert.Single(bang.Players, p => p.Role == BangRole.Sheriff);

        Assert.True(sheriff.RoleRevealed);
        Assert.All(bang.Players.Where(p => p != sheriff), p => Assert.False(p.RoleRevealed));
        Assert.InRange(sheriff.MaxHealth, 4, 5);                      // 캐릭터 3~4 + 보안관 1
        Assert.All(bang.Players.Where(p => p != sheriff), p => Assert.InRange(p.MaxHealth, 3, 4));
        Assert.All(bang.Players, p => Assert.Equal(p.MaxHealth, p.Health));
        Assert.Equal(sheriff.PlayerId, bang.CurrentTurnPlayerId);
        // 시작 손패 = 체력. 보안관은 턴 시작 드로우가 더해지므로 이상
        Assert.All(bang.Players.Where(p => p != sheriff), p => Assert.Equal(p.MaxHealth, p.Hand.Count));
        Assert.True(sheriff.Hand.Count >= sheriff.MaxHealth);
    }

    [Fact]
    public void 방플레이어_정보가_좌석순으로_복사된다()
    {
        var (_, bs, room) = Setup(4);
        bs.StartBangGame(room.RoomId);
        var bang = room.Bang!;
        Assert.True(room.IsStarted);
        Assert.False(room.IsFinished);
        Assert.False(bang.IsFinished);
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(i, bang.Players[i].SeatIndex);
            Assert.Equal(room.Players[i].PlayerId, bang.Players[i].PlayerId);
            Assert.Equal(room.Players[i].Name, bang.Players[i].Name);
            Assert.Equal(room.Players[i].IsBot, bang.Players[i].IsBot);
        }
        Assert.False(bang.Players[0].IsBot);
        Assert.True(bang.Players[1].IsBot);
    }

    [Theory]
    [InlineData(4, false, 80)]
    [InlineData(7, false, 80)]
    [InlineData(4, true, 107)]
    [InlineData(7, true, 107)]
    public void 카드_총량은_시작후에도_보존된다(int players, bool dodge, int expectedTotal)
    {
        // 캐릭터 배정이 랜덤이므로 여러 번 돌려 드로우 특수능력 경로까지 덮는다
        for (int trial = 0; trial < 30; trial++)
        {
            var (_, bs, room) = Setup(players, dodge);
            Assert.True(bs.StartBangGame(room.RoomId));
            var bang = room.Bang!;
            int total = bang.Deck.Count + bang.DiscardPile.Count
                + bang.Players.Sum(p => p.Hand.Count + p.Equipment.Count + (p.Weapon != null ? 1 : 0))
                + (bang.Pending?.RevealedCards.Count ?? 0);
            Assert.Equal(expectedTotal, total);
            Assert.Equal(expectedTotal, bang.NextCardId - 1);
            Assert.Equal(dodge, bang.UseDodgeCity);
        }
    }
}
