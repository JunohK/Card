using Microsoft.EntityFrameworkCore;
using Card.Api.Models;

namespace Card.Api.Data;

/// <summary>
/// EF Core 메인 DB 컨텍스트
/// </summary>
public class GameDbContext : DbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<GameCard> Cards => Set<GameCard>();
    public DbSet<Deck> Decks => Set<Deck>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<MatchPlayer> MatchPlayers => Set<MatchPlayer>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseSqlite("Data Source=cardgame.db");
    }
}
