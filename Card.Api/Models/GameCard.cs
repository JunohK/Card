namespace Card.Api.Models;

/// <summary>
/// 게임 카드 정의 (정적 데이터)
/// </summary>
public class GameCard
{
    public int Id { get; set; }

    // 카드 이름
    public string Name { get; set; } = "";

    // 카드 비용
    public int Cost { get; set; }

    // 카드 효과 타입 (나중에 enum 추천)
    // public string EffectType { get; set; } = "";

    // 효과 수치
    // public int EffectValue { get; set; }
}
