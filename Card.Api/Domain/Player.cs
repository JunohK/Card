// Player.cs
namespace Card.Api.Domain;

public class Player
{
    public string PlayerId { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";

    // 손에 있는 카드
    public List<PlayingCard> Hand { get; set; } = new(); 

    // 준비 여부
    public bool isReady { get; set; } = false; 

    // 손에 2장만 남고 같은 카드인 경우
    public bool IsWaitingFinalWin { get; set;} = false;

    // 통계 데이터
    public int Wins { get; set; } = 0;
    public int TotalGames { get; set; } = 0;
    public int MaxScore { get; set; } = 0;
    public int MinScore { get; set; } = 0;
    // 승률 계산
    public double WinRate => TotalGames > 0 ? Math.Round((double)Wins / TotalGames * 100, 1) : 0;

    // 점수 관리 (Score는 이번 라운드 점수, TotalScore는 누적용으로 사용)
    public int Score { get; set; } = 0;
    public int TotalScore { get; set; } = 0;
    public int RoundTurnCount { get; set; } = 0;
    public bool AllowNaturalBagaji { get; set; } = true;

    // AI/봇 플레이어 여부 — 방장이 추가, 게임 시작 시 BangPlayer.IsBot으로 전파
    public bool IsBot { get; set; } = false;

    // ── 직렬화 뷰 전용 필드 (CreateViewFor에서 설정) ──
    // 손패 장수 — 다른 플레이어에게는 Hand 대신 이 값만 공개
    public int HandCount { get; set; }
    // 바가지 상태(2장 + 같은 숫자 또는 조커 포함) — 손패 내용 대신 파생 불리언만 공개
    // (기존 UI가 상대 손패를 직접 읽어 표시하던 정보를 보존)
    public bool IsBagaji { get; set; }

    /// <summary>viewer 관점의 직렬화용 뷰 — 본인이 아니면 손패 내용을 숨긴다.</summary>
    public Player CreateViewFor(string viewerPlayerId)
    {
        var view = (Player)MemberwiseClone();
        var hand = Hand ?? new List<PlayingCard>();
        view.HandCount = hand.Count;
        view.IsBagaji = hand.Count == 2 &&
            (hand.Any(c => c.Rank == "Joker" || c.Rank == "JK") || hand[0].Rank == hand[1].Rank);
        if (PlayerId != viewerPlayerId)
            view.Hand = new List<PlayingCard>();
        return view;
    }
}