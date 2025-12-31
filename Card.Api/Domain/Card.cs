// 카드 정의
namespace Card.Api.Domain;

public class Card
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string suit { get; set; } = ""; // Spade, Heart, Diamond, Club
    public string Rank { get; set; } = ""; // A, 2~10,J,Q,K
}