// GameRoom.cs
namespace Card.Api.Domain;

public enum WinReason
{
    SixCardImmediate,
    FinalWaitInterrupt,
    TripleInterrupt,
    Straight,
    ManualDeclare
}

public enum CardMode
{
    Trump = 0,  // 1~13 (A~K)
    Hwatu = 1   // 1~12 (K 제외). 룰은 트럼프와 동일하나 sum 임계값만 다름
}

public enum GameMode
{
    Cards = 0,  // 트럼프/화투 카드 게임 (기존)
    Omok = 1,   // 오목 (15x15 렌주 룰)
    Bang = 2    // 뱅 (보드게임) — MVP
}

// 라운드 종료 시 점수 스냅샷 — 인게임 점수 히스토리 모달용
public class PlayerRoundScore
{
    public string PlayerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Score { get; set; }       // 이번 라운드 획득/감점 (음수=감점)
    public int TotalScore { get; set; }  // 누적 총점 (라운드 종료 시점)
}

public class RoundScoreEntry
{
    public int Round { get; set; }
    public string? WinnerName { get; set; }
    public string? WinType { get; set; }
    public List<PlayerRoundScore> Scores { get; set; } = new();
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
    public int RoundTurnSequence { get; set; } = 0;

    // 조커 갯수 (0, 1, 2). 방장이 RoomPage에서 변경 가능. 기본값 1
    public int JokerCount { get; set; } = 1;

    // 카드 모드: Trump (1~13) / Hwatu (1~12). 기본 Trump
    public CardMode CardMode { get; set; } = CardMode.Trump;

    // 라운드별 점수 히스토리 (게임 시작 시 초기화, 매 라운드 종료 시 append)
    public List<RoundScoreEntry> RoundScoreHistory { get; set; } = new();

    // 게임 모드 (Cards / Omok). 방 생성 시 결정, 변경 불가
    public GameMode GameMode { get; set; } = GameMode.Cards;

    // 오목 모드 전용 상태 (Cards 모드면 null)
    public OmokState? Omok { get; set; }

    // 뱅 모드 전용 상태
    public BangState? Bang { get; set; }

    // 뱅 — Dodge City 확장팩 사용 여부 (방장이 게임 시작 전 토글)
    public bool BangUseDodgeCity { get; set; } = false;

    // 뱅 — 응답 타이머 한도(초). 방장이 설정. 10/20/30/60 중 하나, 기본 30
    public int BangResponseTimerSec { get; set; } = 30;

    // 턴 타임아웃(초) — 현재 턴 플레이어가 행동하지 않으면 자동 진행. 모든 모드 공통, 기본 60
    public int TurnTimeoutSec { get; set; } = 60;

    // Cards 모드 현재 턴 시작 시각(UTC). 턴 타임아웃 서버 검증용. (Bang/Omok은 각 하위 상태에 보관)
    public DateTime TurnStartedAt { get; set; } = DateTime.UtcNow;

    public bool IsGameOver => IsFinished;
    // public bool IsGameOver { get; set; }
    public int DeckCount => Deck.Count;
    public string LastActorPlayerId { get; set; } = string.Empty;
    public bool IsStopDeclared { get; set; } = false;
    public string StopCallerId { get; set; } = "";

    // 뻥/가로채기 윈도우 atomic flag (0=닫힘, 1=열림)
    // Interlocked.CompareExchange로만 접근 — 동시 InterruptDiscard/InterceptWin race 방지용
    [System.Text.Json.Serialization.JsonIgnore]
    public int PungWindowFlag;
}