using Card.Api.Models;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Card.Api.Services;

public class JwtTokenService
{
    private readonly string _key;
    private readonly string _issuer;

    public JwtTokenService(IConfiguration config)
    {
        _key = config["jwt:key"] ?? "DEV_SECRET_KEY_1234566789";
        _issuer = "CardGameServer";
    }

    public string CreateToken(User user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Nickname)
        };

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_key));

        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _issuer,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(6),
            signingCredentials: creds
        );
        
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}