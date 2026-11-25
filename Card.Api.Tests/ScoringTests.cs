using Card.Api.Domain;
using Card.Api.Services;
using static Card.Api.Tests.Cards;

namespace Card.Api.Tests;

// GameRoomService.Scoring 특성화 테스트 — 현재 동작을 그대로 고정한다.
public class ScoringTests
{
    private readonly GameRoomService _svc = new();

    [Fact]
    public void Straight_6연속이면_구간합의_음수()
    {
        var r = _svc.CheckWinCondition(Hand("3", "4", "5", "6", "7", "8"));
        Assert.Equal((true, "스트레이트", -33), r);
    }

    [Fact]
    public void Straight_조커는_합이_가장_큰_구간으로_쓰인다()
    {
        // 3~7 + 조커: 2~7(합 27) 또는 3~8(합 33) → 33
        var r = _svc.CheckWinCondition(Hand("3", "4", "5", "6", "7", "Joker"));
        Assert.Equal((true, "스트레이트", -33), r);
    }

    [Fact]
    public void HighSum_트럼프는_68이상()
    {
        var r = _svc.CheckWinCondition(Hand("K", "K", "Q", "Q", "J", "J"));
        Assert.Equal((true, "68-", -72), r);
    }

    [Fact]
    public void HighSum_화투는_65_임계값_트럼프에서는_222로_떨어진다()
    {
        var hand = Hand("Q", "Q", "J", "J", "10", "10"); // 합 66
        Assert.Equal((true, "65-", -66), _svc.CheckWinCondition(hand, CardMode.Hwatu));
        Assert.Equal((true, "2 + 2 + 2", 0), _svc.CheckWinCondition(hand, CardMode.Trump));
    }

    [Fact]
    public void LowSum과_4더하기2_동시면_200()
    {
        var r = _svc.CheckWinCondition(Hand("A", "A", "A", "A", "2", "2"));
        Assert.Equal((true, "200-", -200), r);
    }

    [Fact]
    public void LowSum_10이하만이면_100()
    {
        var r = _svc.CheckWinCondition(Hand("A", "A", "A", "2", "2", "3"));
        Assert.Equal((true, "100-", -100), r);
    }

    [Fact]
    public void 네장더하기두장은_마이너스100()
    {
        var r = _svc.CheckWinCondition(Hand("9", "9", "9", "9", "K", "K"));
        Assert.Equal((true, "4 + 2", -100), r);
    }

    [Fact]
    public void 네장더하기두장_조커로_채움()
    {
        var r = _svc.CheckWinCondition(Hand("7", "7", "7", "Joker", "2", "2"));
        Assert.Equal((true, "4 + 2", -100), r);
    }

    [Fact]
    public void 세장더하기세장은_0점()
    {
        var r = _svc.CheckWinCondition(Hand("5", "5", "5", "9", "9", "9"));
        Assert.Equal((true, "3 + 3", 0), r);
    }

    [Fact]
    public void 족보없으면_None()
    {
        var r = _svc.CheckWinCondition(Hand("2", "5", "7", "9", "J", "K"));
        Assert.Equal((false, "None", 0), r);
    }

    [Fact]
    public void 여섯장_미만이면_None()
    {
        Assert.Equal((false, "None", 0), _svc.CheckWinCondition(Hand("2", "5")));
        Assert.Equal((false, "None", 0), _svc.CheckWinCondition(new List<PlayingCard>()));
    }

    [Theory]
    [InlineData(25, "K", "Q")]                      // 세트 없음 → 합산
    [InlineData(15, "5", "5", "5", "K", "2")]       // 3장 세트 제외
    [InlineData(0, "7", "7", "7", "7")]             // 4장 세트 제외
    [InlineData(3, "9", "9", "Joker", "3")]         // 2장 + 조커 = 세트
    [InlineData(2, "Joker", "Joker")]               // 남은 조커 1점씩
    [InlineData(2, "A", "Joker")]                   // A(1) + 남은 조커(1)
    [InlineData(20, "10", "10")]
    public void CalculateLoserScore_현재규칙(int expected, params string[] ranks)
    {
        Assert.Equal(expected, _svc.CalculateLoserScore(Hand(ranks)));
    }

    [Theory]
    [InlineData("Joker", true)]
    [InlineData("JK", true)]
    [InlineData("joker1", true)]
    [InlineData("J", false)]
    [InlineData("", false)]
    public void IsJoker_랭크문자열_판정(string rank, bool expected)
    {
        Assert.Equal(expected, GameRoomService.IsJoker(C(rank)));
    }
}
