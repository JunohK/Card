using Card.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Card.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LeaderboardController : ControllerBase
{
    private readonly GameDbContext _db;

    public LeaderboardController(GameDbContext db)
    {
        _db = db;
    }

    // GET /api/leaderboard/top?limit=10
    [HttpGet("top")]
    public async Task<ActionResult<object>> GetTop([FromQuery] int limit = 10)
    {
        if (limit <= 0 || limit > 100) limit = 10;

        // Wins 내림차순, 동률은 totalGames 적은 순 (효율 높음), 그 다음 maxScore 내림차순
        var top = await _db.Users
            .OrderByDescending(u => u.Wins)
            .ThenBy(u => u.TotalGames)
            .ThenByDescending(u => u.MaxScore)
            .Take(limit)
            .Select(u => new
            {
                u.Nickname,
                u.Wins,
                u.TotalGames,
                u.MaxScore,
                u.MinScore,
                WinRate = u.TotalGames > 0 ? (double)u.Wins / u.TotalGames : 0.0
            })
            .ToListAsync();

        return Ok(top);
    }
}
