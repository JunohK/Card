namespace Card.Api.Models;

public class User
{
    public int Id { get; set; }
    public string PasswordHash { get; set; } = "";
    public string Nickname { get; set; } = "";
}
