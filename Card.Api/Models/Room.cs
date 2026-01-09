using Card.Api.Domain;

namespace Card.Api.Models;

public class Room
{
    // public string RoomId { get; set; } = Guid.NewGuid().ToString("N")[..6];
    public string RoomId { get; set; } = Guid.NewGuid().ToString("N")[..6];
    public string Title { get; set; } = "";
    public string? Password { get; set; }
    public bool IsStarted {get; set; }
    public string Name { get; set; } = "";
    public List<string> Users { get; set; } = new();
    public List<Player> Players { get; set; } = new();
}