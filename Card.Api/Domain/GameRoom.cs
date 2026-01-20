namespace Card.Api.Domain;

public class GameRoom
{
    public string RoomId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Password { get; set; }
    public List<Player> Players { get; set; } = new();
    public string? HostPlayerId { get; set; }
    public bool IsStarted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<PlayingCard> Deck { get; set; } = new();
    public List<PlayingCard> DiscardPile { get; set; } = new();
    public PlayingCard? LastDiscardedCard { get; set; }
    public string? CurrentTurnPlayerId { get; set; }

    // ✅ { get; set; } 이 반드시 있어야 서비스에서 room.IsFinished = true; 가 가능합니다.
    public bool IsFinished { get; set; } 
    public string? WinnerPlayerId { get; set; }
    public WinReason? WinReason { get; set; }
    public string? WinnerName { get; set; }

    // 프론트엔드 호환용 (읽기 전용 별칭)
    public bool IsGameOver { get; set; }
    public int DeckCount => Deck?.Count ?? 0;
}

public enum WinReason
{
    SixCardImmediate,
    FinalWaitInterrupt,
    TripleInterrupt,
    Straight,
    ManualDeclare
}