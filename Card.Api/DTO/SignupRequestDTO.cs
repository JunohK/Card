namespace Card.Api.DTO;

public class SignupRequestDTO
{
    public string Password { get; set; } = null!;
    public string Nickname { get; set; } = null!;
}