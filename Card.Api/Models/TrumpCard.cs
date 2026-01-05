namespace Card.Api.Models;

/// <summary>
/// 트럼프 카드
/// DB 저장 X, 메모리용
/// </summary>

public class TrumpCard
{
    public string Suit { get; set; } = ""; // Hearts, Diamonds, Clubs, Spades
    public String Rank { get; set; } = ""; // A, 2 ~ 10, J, Q, K

    public override string ToString()
    {
        return $"{Suit}-{Rank}";
    }
}