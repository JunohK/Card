// 플레이어
namespace Card.Api.Domain;

public class Player
{
    public string PlayerId { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";

    // 손에 있는 카드
    public List<PlayingCard> Hand { get; set; } = new(); 

    // 준비 여부
    public bool isReady { get; set; } = false; 

    // 손에 2장만 남고 같은 카드인 경우
    public bool IsWaitingFinalWin { get; set;} = false;

    // 누적 점수
    public int Score { get; set; } = 0;
}