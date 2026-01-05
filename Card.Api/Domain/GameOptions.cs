namespace Card.Api.Domain;

public enum JokerMode
{
    None = 0,
    One = 1,
    Two = 2
}

public class GameOptions
{
    public int MaxPlayers { get; set; } = 7;
    public JokerMode jokerMode { get; set; } = JokerMode.None;
    public int TotalRounds { get; set; } = 1; // 1, 5, 10
}