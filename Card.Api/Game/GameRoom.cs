namespace CardGameServer.Game;

public class GameRoom
{
    public string RoomId { get; set; } = "";
    public List<PlayerState> Players { get; set; } = new();
    public int CurrentTurnIndex { get; set; }
}
