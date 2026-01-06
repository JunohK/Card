using Microsoft.AspNetCore.Identity;

namespace Card.Api.Services;

public class PasswordHashService
{
    private readonly PasswordHasher<string> _hasher = new();

    public string Hash(string password)
    {
        return _hasher.HashPassword("user", password);
    }

    public bool Verify(string hash, string password)
    {
        return _hasher.VerifyHashedPassword("user", hash, password)
            == PasswordVerificationResult.Success;
    }
}