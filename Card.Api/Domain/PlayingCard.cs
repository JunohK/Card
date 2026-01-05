// 카드 정의
namespace Card.Api.Domain;

public class PlayingCard
{
    public CardType Type { get; set; }
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Suit { get; set; } = ""; // Spade, Heart, Diamond, Club
    public string Rank { get; set; } = ""; // A, 2~10,J,Q,K

    public int? Value { get; set; } // A = 1, J = 11, Q = 12, K = 13, Joker는 어떤 숫자든 가능

    public bool IsJoker => Type == CardType.Joker;
    public override string ToString()
    {
        return $"{Suit}-{Rank}";
    }
}

public enum CardType
{
    Normal,
    Joker
}