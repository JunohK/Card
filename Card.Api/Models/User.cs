using System.ComponentModel.DataAnnotations;

namespace Card.Api.Models;

public class User
{
    public int Id { get; set; }

    [Required]
    public string PasswordHash { get; set; } = null!;

    [Required]
    [MaxLength(100)]
    public string Nickname { get; set; } = null!;

    public DateTime CreatedAt { get; set; }= DateTime.UtcNow;
}