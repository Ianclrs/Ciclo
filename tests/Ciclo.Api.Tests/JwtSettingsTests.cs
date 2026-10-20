using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Ciclo.Infrastructure.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace Ciclo.Api.Tests;

/// <summary>
/// C1 — o segredo de JWT é obrigatório, não tem valor padrão e é validado antes de a API
/// atender qualquer requisição.
///
/// Contexto: <c>appsettings.json</c> trazia
/// <c>"Secret": "fallback-dev-secret-at-least-32-chars!!"</c>, versionado. Como o código só
/// lançava quando a chave estava <i>ausente</i>, qualquer ambiente que não a sobrescrevesse
/// assinava e aceitava tokens com esse valor público — bypass total de autenticação.
/// </summary>
public class JwtSettingsTests
{
    private const string ValidSecret = "test-secret-that-is-at-least-32-characters-long!!";

    /// <summary>Valor que estava versionado em <c>appsettings.json</c> antes da correção.</summary>
    private const string FormerPublicFallbackSecret = "fallback-dev-secret-at-least-32-chars!!";

    [Fact]
    public void Read_Throws_WhenSecretIsMissing()
    {
        var config = CreateConfiguration(secret: null);

        var ex = Assert.Throws<InvalidOperationException>(() => JwtSettings.Read(config));

        Assert.Contains(JwtSettings.SecretKey, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Read_Throws_WhenSecretIsBlank(string secret)
    {
        var config = CreateConfiguration(secret);

        Assert.Throws<InvalidOperationException>(() => JwtSettings.Read(config));
    }

    [Fact]
    public void Read_Throws_WhenSecretIsShorterThan32Bytes()
    {
        // 31 caracteres ASCII = 31 bytes, abaixo do mínimo do HMAC-SHA256.
        var config = CreateConfiguration("short-secret-with-31-chars!!!");

        var ex = Assert.Throws<InvalidOperationException>(() => JwtSettings.Read(config));

        Assert.Contains("32", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_ReturnsConfiguredValues_WhenConfigurationIsValid()
    {
        var config = CreateConfiguration(ValidSecret);

        var settings = JwtSettings.Read(config);

        Assert.Equal(ValidSecret, settings.Secret);
        Assert.Equal("Ciclo", settings.Issuer);
        Assert.Equal("Ciclo", settings.Audience);
    }

    [Fact]
    public void TokenSignedWithTheConfiguredSecret_IsAccepted()
    {
        var settings = JwtSettings.Read(CreateConfiguration(ValidSecret));
        var handler = new JwtSecurityTokenHandler();

        var token = SignToken(ValidSecret, settings.Issuer, settings.Audience);

        var principal = handler.ValidateToken(token, settings.CreateValidationParameters(), out _);

        // Controle positivo: sem ele, o teste abaixo passaria mesmo se a validação estivesse
        // desligada e nada fosse aceito.
        Assert.True(principal.Identity?.IsAuthenticated);
    }

    [Fact]
    public void TokenSignedWithTheFormerPublicFallbackSecret_IsRejected()
    {
        var settings = JwtSettings.Read(CreateConfiguration(ValidSecret));
        var handler = new JwtSecurityTokenHandler();

        var forged = SignToken(
            FormerPublicFallbackSecret, settings.Issuer, settings.Audience,
            role: "Admin", tenantId: Guid.NewGuid());

        Assert.ThrowsAny<SecurityTokenException>(
            () => handler.ValidateToken(forged, settings.CreateValidationParameters(), out _));
    }

    private static IConfiguration CreateConfiguration(
        string? secret, string? issuer = "Ciclo", string? audience = "Ciclo")
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c[It.IsAny<string>()]).Returns((string key) => key switch
        {
            JwtSettings.SecretKey => secret,
            JwtSettings.IssuerKey => issuer,
            JwtSettings.AudienceKey => audience,
            _ => null
        });
        return config.Object;
    }

    private static string SignToken(
        string secret, string issuer, string audience,
        string role = "Staff", Guid? tenantId = null)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims:
            [
                new Claim("role", role),
                new Claim("tenant_id", (tenantId ?? Guid.NewGuid()).ToString())
            ],
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
