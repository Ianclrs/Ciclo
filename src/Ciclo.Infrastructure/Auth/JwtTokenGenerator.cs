using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Ciclo.Core.Entities;

namespace Ciclo.Infrastructure.Auth;

public interface IJwtTokenGenerator
{
    string GenerateAccessToken(User user);
    RefreshToken GenerateRefreshToken(Guid userId);
}

public class JwtTokenGenerator : IJwtTokenGenerator
{
    private readonly IConfiguration _configuration;

    public JwtTokenGenerator(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string GenerateAccessToken(User user)
    {
        var settings = JwtSettings.Read(_configuration);

        var credentials = new SigningCredentials(settings.CreateSigningKey(), SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new Claim("tenant_id", user.TenantId.ToString()),
            new Claim("role", user.Role.ToString()),
            new Claim("name", user.Name)
        };

        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public RefreshToken GenerateRefreshToken(Guid userId)
    {
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);

        return new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = Convert.ToBase64String(randomBytes),
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        };
    }
}
