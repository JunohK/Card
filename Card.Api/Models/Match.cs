namespace Card.Api.Models;

public class Match
{
    public int Id { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime EndedAt { get; set; }

    // public List<MatchPlayer> Players { get; set; } = new();
}
