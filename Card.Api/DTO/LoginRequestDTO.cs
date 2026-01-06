namespace Card.Api.DTO;

public class LoginRequestDTO
{
    public string Nickname { get; set; } = null!;
    public string Password { get; set; } = null!;
}