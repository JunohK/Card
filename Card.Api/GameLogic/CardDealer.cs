using Card.Api.Domain;

namespace Card.Api.GameLogic;

/// <summary>
/// 카드 분배 전용 유틸 (상태 없음)
/// </summary>
public static class CardDealer
{
    public static void DealInitialHands(
        List<Player> players,
        List<PlayingCard> deck,
        int cardsPerPlayer)
    {
        int index = 0;

        foreach (var player in players)
        {
            player.Hand.Clear();

            for (int i = 0; i < cardsPerPlayer; i++)
            {
                player.Hand.Add(deck[index]);
                index++;
            }
        }
    }
}
