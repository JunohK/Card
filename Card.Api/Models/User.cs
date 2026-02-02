using System.ComponentModel.DataAnnotations;

namespace Card.Api.Models;

public class User
{
    public int Id { get; set; }

    [Required]
    public string PasswordHash { get; set; } = null!;

    [Required]
    [MaxLength(100)]
    public string Nickname { get; set; } = null!;

    public DateTime CreatedAt { get; set; }= DateTime.UtcNow;

    // 통계 데이터 필드 추가
    public int Wins { get; set; } = 0;
    public int TotalGames { get; set; } = 0;
    public int MaxScore { get; set; } = 0;
    public int MinScore { get; set; } = 0;
}