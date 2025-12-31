namespace Card.Api.Models;

public class MatchPlayer
{
    public int Id { get; set; }

    // 대결 ID
    public int MatchId { get; set; }
    public int UserId { get; set; }

    // 앉는 자리 index
    public int SeatIndex { get; set; } // 0~6
    public bool IsWinner { get; set; }
}
