using Card.Api.Data;
using Card.Api.DTO;
using Card.Api.Services;
using Card.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.Data;

namespace Card.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly GameDbContext _db;
    private readonly PasswordHashService _passwordHashService;

    public AuthController(GameDbContext db, PasswordHashService passwordHashService)
    {
        _db = db;
        _passwordHashService = passwordHashService;
    }

    // 회원가입
    [HttpPost("signup")]
    public async Task<ActionResult<SignupResponseDTO>> Signup(SignupRequestDTO dto)
    {
        if (await _db.Users.AnyAsync(u => u.Nickname == dto.Nickname))
        {
            return BadRequest("이미 사용중인 닉네임입니다.");
        }

        var user = new User
        {
            Nickname = dto.Nickname,
            PasswordHash = _passwordHashService.Hash(dto.Password)
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return Ok(new SignupResponseDTO
        {
            UserId = user.Id,
            Nickname = user.Nickname
        });
    }

    // 로그인
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponseDTO>> Login(
        LoginRequestDTO dto,
        [FromServices] JwtTokenService jwt
    )
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Nickname == dto.Nickname);

        if(user == null)
            return Unauthorized("존재하지 않는 사용자");

        if(!_passwordHashService.Verify(user.PasswordHash, dto.Password))
            return Unauthorized("비밀번호가 틀렸습니다.");

        var token = jwt.CreateToken(user);

        return Ok(new LoginResponseDTO
        {
            Token = token,
            Nickname = user.Nickname
        });
    }
}