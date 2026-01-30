// GameRoom.cs
using Card.Api.GameLogic;
using Microsoft.AspNetCore.SignalR;

namespace Card.Api.Domain;

public enum WinReason
{
    SixCardImmediate,
    FinalWaitInterrupt,
    TripleInterrupt,
    Straight,
    ManualDeclare
}

public class GameRoom
{
    public string RoomId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Password { get; set; }
    
    // Player.cs를 참조하므로 여기서는 리스트만 선언하면 됩니다.
    public List<Player> Players { get; set; } = new();
    
    public string? HostPlayerId { get; set; }
    public bool IsStarted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<PlayingCard> Deck { get; set; } = new();
    public List<PlayingCard> DiscardPile { get; set; } = new();
    public PlayingCard? LastDiscardedCard { get; set; }
    public string? CurrentTurnPlayerId { get; set; }

    public bool IsFinished { get; set; } 
    public bool IsRoundEnded { get; set; } // 전광판 노출용
    public string? WinnerPlayerId { get; set; }
    public WinReason? WinReason { get; set; }
    public string? WinnerName { get; set; }
    public List<PlayingCard> WinnerHand { get; set; } = new List<PlayingCard>();
    public string? LastWinType { get; set; } 
    public int MaxRounds { get; set; }
    public int CurrentRound { get; set; }

    public bool IsGameOver => IsFinished;
    // public bool IsGameOver { get; set; }
    public int DeckCount => Deck.Count;
    public string LastActorPlayerId { get; set; } = string.Empty;
    public bool IsStopDeclared { get; set; } = false;
    public string StopCallerId { get; set; } = "";
}