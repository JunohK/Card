using Card.Api.Models;

namespace Card.Api.Data.Seed;

/// <summary>
/// DB 초기 데이터 삽입용 클래스
/// 서버 최초 실행 시 카드 더미 생성
/// </summary>

public static class DbInitializer
{
    /// <summary>
    /// DB에 카드가 없으면 기본 카드 데이터 삽입
    /// </summary>
    
    public static void Seed(GameDbContext context)
    {
        // DB 생성 보장
        context.Database.EnsureCreated();

        // 이미 카드가 있으면 Seed 하지 않음(중복방지)
        if(context.Cards.Any())
            return;

        // 기본 카드 목록
        var cards = new List<GameCard>
        {
            // Spade
            new GameCard { Name = "SpadeA", Cost = 1},
            new GameCard { Name = "Spade2", Cost = 2},
            new GameCard { Name = "Spade3", Cost = 3},
            new GameCard { Name = "Spade4", Cost = 4},
            new GameCard { Name = "Spade5", Cost = 5},
            new GameCard { Name = "Spade6", Cost = 6},
            new GameCard { Name = "Spade7", Cost = 7},
            new GameCard { Name = "Spade8", Cost = 8},
            new GameCard { Name = "Spade9", Cost = 9},
            new GameCard { Name = "Spade10", Cost = 10},
            new GameCard { Name = "SpadeJ", Cost = 11},
            new GameCard { Name = "SpadeQ", Cost = 12},
            new GameCard { Name = "SpadeK", Cost = 13},

            // Clober
            new GameCard { Name = "CloberA", Cost = 1},
            new GameCard { Name = "Clober2", Cost = 2},
            new GameCard { Name = "Clober3", Cost = 3},
            new GameCard { Name = "Clober4", Cost = 4},
            new GameCard { Name = "Clober5", Cost = 5},
            new GameCard { Name = "Clober6", Cost = 6},
            new GameCard { Name = "Clober7", Cost = 7},
            new GameCard { Name = "Clober8", Cost = 8},
            new GameCard { Name = "Clober9", Cost = 9},
            new GameCard { Name = "Clober10", Cost = 10},
            new GameCard { Name = "CloberJ", Cost = 11},
            new GameCard { Name = "CloberQ", Cost = 12},
            new GameCard { Name = "CloberK", Cost = 13},

            // Diamond
            new GameCard { Name = "DiamondA", Cost = 1},
            new GameCard { Name = "Diamond2", Cost = 2},
            new GameCard { Name = "Diamond3", Cost = 3},
            new GameCard { Name = "Diamond4", Cost = 4},
            new GameCard { Name = "Diamond5", Cost = 5},
            new GameCard { Name = "Diamond6", Cost = 6},
            new GameCard { Name = "Diamond7", Cost = 7},
            new GameCard { Name = "Diamond8", Cost = 8},
            new GameCard { Name = "Diamond9", Cost = 9},
            new GameCard { Name = "Diamond10", Cost = 10},
            new GameCard { Name = "DiamondJ", Cost = 11},
            new GameCard { Name = "DiamondQ", Cost = 12},
            new GameCard { Name = "DiamondK", Cost = 13},

            // Heart
            new GameCard { Name = "HeartA", Cost = 1},
            new GameCard { Name = "Heart2", Cost = 2},
            new GameCard { Name = "Heart3", Cost = 3},
            new GameCard { Name = "Heart4", Cost = 4},
            new GameCard { Name = "Heart5", Cost = 5},
            new GameCard { Name = "Heart6", Cost = 6},
            new GameCard { Name = "Heart7", Cost = 7},
            new GameCard { Name = "Heart8", Cost = 8},
            new GameCard { Name = "Heart9", Cost = 9},
            new GameCard { Name = "Heart10", Cost = 10},
            new GameCard { Name = "HeartJ", Cost = 11},
            new GameCard { Name = "HeartQ", Cost = 12},
            new GameCard { Name = "HeartK", Cost = 13},

            // Joker
            new GameCard { Name = "Joker1", Cost = 20},
            new GameCard { Name = "Joker2", Cost = 21},
            new GameCard { Name = "Joker3", Cost = 22},
        };

        // DB에 추가
        context.Cards.AddRange(cards);

        // 실제 저장
        context.SaveChanges();
    }
}