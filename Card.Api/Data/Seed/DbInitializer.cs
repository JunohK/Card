using Card.Api.Models;

namespace Card.Api.Data.Seed;

/// <summary>
/// DB 초기 데이터 삽입용 클래스
/// 서버 최초 실행 시 카드 더미 생성
/// </summary>

public static class DbInitializer
{
    /// <summary>
    /// DB에 카드가 없으면 기본 카드 데이터를 삽입한다
    /// </summary>
    
    public static void Seed(GameDbContext context)
    {
        // DB 생성 보장
        context.Database.EnsureCreated();

        // 이미 카드가 있으면 Seed 하지 않음(중복방지)
        if(context.Cards.Any())
            return;

        // 기본 카드 목록
        var cards = new List<Card>
        {
            new Card { Name = "", Cost = 1},

        };

        // DB에 추가
        context.Cards.AddRange(cards);

        // 실제 저장
        context.SaveChanges();
    }
}