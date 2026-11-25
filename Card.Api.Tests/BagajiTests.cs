using Card.Api.Domain;
using Card.Api.Services;
using static Card.Api.Tests.Cards;

namespace Card.Api.Tests;

// GameRoomService.CheckBagajiWin 특성화 테스트
public class BagajiTests
{
    private readonly GameRoomService _svc = new();

    private static Player P(string id, List<PlayingCard> hand, bool natural = true) =>
        new() { PlayerId = id, Name = id, Hand = hand, AllowNaturalBagaji = natural };

    [Fact]
    public void 일반바가지_2장_같은랭크()
    {
        var players = new List<Player> { P("a", Hand("8", "8")), P("b", Hand("2", "3", "4", "5", "6")) };
        Assert.Equal((true, "a", "b"), _svc.CheckBagajiWin("8", "b", players));
    }

    [Fact]
    public void 일반바가지_1장_조커()
    {
        var players = new List<Player> { P("a", Hand("8", "Joker")), P("b", Hand("2", "3", "4", "5", "6")) };
        Assert.Equal((true, "a", "b"), _svc.CheckBagajiWin("8", "b", players));
    }

    [Fact]
    public void 랭크가_다르면_불성립()
    {
        var players = new List<Player> { P("a", Hand("8", "8")), P("b", Hand("2", "3", "4", "5", "6")) };
        Assert.False(_svc.CheckBagajiWin("9", "b", players).isBagajiWin);
    }

    [Fact]
    public void 본인이_버린_카드로는_불성립()
    {
        var players = new List<Player> { P("a", Hand("8", "8")) };
        Assert.False(_svc.CheckBagajiWin("8", "a", players).isBagajiWin);
    }

    [Fact]
    public void 조커를_버리면_불성립()
    {
        var players = new List<Player> { P("a", Hand("Joker", "Joker")), P("b", Hand("2")) };
        Assert.False(_svc.CheckBagajiWin("Joker", "b", players).isBagajiWin);
    }

    [Fact]
    public void 자연바가지_3장과_2장_구성에서_2장쪽만_성립()
    {
        var players = new List<Player> { P("a", Hand("4", "4", "4", "9", "9")), P("b", Hand("2")) };
        Assert.Equal((true, "a", "b"), _svc.CheckBagajiWin("9", "b", players));
        Assert.False(_svc.CheckBagajiWin("4", "b", players).isBagajiWin); // 3장 쪽은 현재 불성립
    }

    [Fact]
    public void 자연바가지_조커1장_2장2장()
    {
        var players = new List<Player> { P("a", Hand("4", "4", "9", "9", "Joker")), P("b", Hand("2")) };
        Assert.True(_svc.CheckBagajiWin("9", "b", players).isBagajiWin);
        Assert.True(_svc.CheckBagajiWin("4", "b", players).isBagajiWin);
    }

    [Fact]
    public void 자연바가지_옵션_끄면_불성립()
    {
        var players = new List<Player> { P("a", Hand("4", "4", "4", "9", "9"), natural: false), P("b", Hand("2")) };
        Assert.False(_svc.CheckBagajiWin("9", "b", players).isBagajiWin);
    }
}
