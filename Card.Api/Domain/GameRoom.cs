using Card.Api.Domain;

public class GameRoom
{
    public string RoomId { get; set; } = Guid.NewGuid().ToString();

    public string Title { get; set; } = "";
    public string? Password { get; set; }
    public List<Player> Players { get; set; } = new();

    public List<PlayingCard> Deck { get; set; } = new();

    public List<PlayingCard> DiscardPile { get; } = new();

    public PlayingCard? LastDiscardedCard { get; set; }

    public string? CurrentTurnPlayerId { get; set; }

    public bool IsStarted { get; set; }
    public bool IsFinished { get; set; }

    public int CurrentRound { get; set; } = 1;
    public int TotalRounds { get; set; } = 3;

    public string? WinnerPlayerId { get; set; }

    public WinReason? WinReason { get; set; }
}

public enum WinReason
{
    SixCardImmediate,     // 6장 즉시 종료
    FinalWaitInterrupt,   // 2장 대기 상태 인터럽트
    TripleInterrupt,      // 3장 트리플 인터럽트
    Straight,             // 스트레이트
    ManualDeclare         // DeclareWin 버튼
}
