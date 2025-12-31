// 게임 룸

namespace Card.Api.Domain;

public class GameRoom
{
    public string RoomId { get; set; } = Guid.NewGuid().ToString();
    public List<Player> Players { get; set; } = new();
    public Stack<Card> Deck { get; set; } = new();
    public List<Card> DiscardPile { get; set; } = new();
    public GameState State { get; set; } = new();
}