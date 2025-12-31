namespace CardGameServer.Game;

public class PlayerState
{
    public int UserId { get; set; }
    public int HP { get; set; }
    public List<int> HandCardIds { get; set; } = new();
}
