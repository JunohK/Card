namespace Card.Api.DTO;

public class LoginResponseDTO
{
    public string Token { get; set; } = null!;
    public string Nickname { get; set; } = null!;
}