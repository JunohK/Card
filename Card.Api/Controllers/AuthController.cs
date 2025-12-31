using Microsoft.AspNetCore.Mvc;
using Card.Api.Data;

namespace Card.Api.Controllers;

/// <summary>
/// 로그인 / 회원가입 API
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly GameDbContext _db;

    public AuthController(GameDbContext db)
    {
        _db = db;
    }

    // [HttpPost("login")]
    // public IActionResult Login(string email, string password)
    // {
    //     // TODO: 비밀번호 검증
    //     var user = _db.Users.FirstOrDefault(u => u.Email == email);

    //     if (user == null)
    //         return Unauthorized();

    //     return Ok(user.Id);
    // }
}
