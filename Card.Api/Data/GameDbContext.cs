using Microsoft.EntityFrameworkCore;
using Card.Api.Models;

namespace Card.Api.Data;

/// <summary>
/// EF Core 메인 DB 컨텍스트
/// </summary>
public class GameDbContext : DbContext
{
    public GameDbContext(DbContextOptions<GameDbContext> options)
        : base(options){}
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<GameCard> Cards => Set<GameCard>();
    // Deck, Match, MatchPlayer DbSet 제거 — 모델 코드 제거에 맞춰 등록 해제.
    // DB 테이블은 남아있음(orphan). 차후 EF 마이그레이션으로 drop 필요.
}
