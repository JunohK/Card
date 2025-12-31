// 플레이어
namespace Card.Api.Domain;

public class Player
{
    public string PlayerId { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public List<Card> Hand { get; set; } = new(); // 손에 있는 카드
    public bool isReady { get; set; } = false; // 준비 여부
}