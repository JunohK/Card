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
}