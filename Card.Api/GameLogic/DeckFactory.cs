using Card.Api.Domain;

namespace Card.Api.GameLogic;

/// <summary>
/// 트럼프 카드 덱 생성 및 셔플
/// </summary>
public static class DeckFactory
{
    private static readonly string[] Suits =
    {
        "Hearts", "Diamonds", "Clubs", "Spades"
    };

    private static readonly string[] Ranks =
    {
        "A", "2", "3", "4", "5", "6", "7", "8", "9",
        "10", "J", "Q", "K", "Joker1", "Joker2"
    };

    /// <summary>
    /// 트럼프 카드 52장 생성 후 셔플
    /// </summary>
    public static List<PlayingCard> CreateShuffledDeck()
    {
        var deck = new List<PlayingCard>();

        foreach (var suit in Suits)
        {
            foreach (var rank in Ranks)
            {
                deck.Add(new PlayingCard
                {
                    Suit = suit,
                    Rank = rank
                });
            }
        }

        // 간단한 셔플
        return deck.OrderBy(_ => Guid.NewGuid()).ToList();
    }

    // 덱 생성(조커)
    public static List<PlayingCard> CreateDeck(JokerMode jokerMode)
    {
        var deck = new List<PlayingCard>();

        foreach (var suit in Suits)
        {
            for(int v = 1; v <= 13; v++)
            {
                deck.Add(new PlayingCard
                {
                    Type = CardType.Normal,
                    Value = v,
                    Suit = suit
                });
            }
        }

        // Joker 추가
        for(int i = 0; i < (int)jokerMode; i++)
        {
            deck.Add(new PlayingCard
            {
                Type = CardType.Joker,
                Value = null,
                Suit = "Joker"
            });
        }

        return CreateShuffledDeck(deck);
    }

    private static List<PlayingCard> CreateShuffledDeck(List<PlayingCard> deck)
{
    var rnd = new Random();
    return deck
        .OrderBy(_ => rnd.Next())
        .ToList();
}
}
