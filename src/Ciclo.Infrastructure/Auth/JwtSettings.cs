using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Ciclo.Infrastructure.Auth;

/// <summary>
/// Configuração de JWT lida da configuração da aplicação, com validação fail-closed.
///
/// O segredo é obrigatório e <b>não tem valor padrão</b>. Uma chave embutida no repositório
/// permite forjar tokens com <c>role</c> e <c>tenant_id</c> arbitrários em qualquer ambiente
/// que não a sobrescreva — ou seja, bypass total de autenticação. Por isso a leitura lança
/// em vez de usar fallback: a API não inicia sem um segredo explícito, fornecido por
/// user-secrets (Development) ou pela variável de ambiente <c>Jwt__Secret</c> (produção).
///
/// Este tipo é o dono único do nome das chaves e dos parâmetros de validação do token,
/// para que o host (Program) e o gerador de tokens não possam divergir.
/// </summary>
public sealed record JwtSettings(string Secret, string Issuer, string Audience)
{
    /// <summary>HMAC-SHA256 exige chave de pelo menos 256 bits (32 bytes).</summary>
    public const int MinimumSecretBytes = 32;

    public const string SecretKey = "Jwt:Secret";
    public const string IssuerKey = "Jwt:Issuer";
    public const string AudienceKey = "Jwt:Audience";

    /// <summary>Chave completa, como declarada na configuração.</summary>
    public const string SecretEnvironmentVariable = "Jwt__Secret";

    /// <summary>
    /// Lê e valida a configuração de JWT. Lança <see cref="InvalidOperationException"/> quando
    /// qualquer chave obrigatória está ausente, vazia ou curta demais.
    /// </summary>
    public static JwtSettings Read(IConfiguration configuration)
    {
        var secret = Require(configuration, SecretKey, SecretEnvironmentVariable);

        var secretBytes = Encoding.UTF8.GetByteCount(secret);
        if (secretBytes < MinimumSecretBytes)
        {
            throw new InvalidOperationException(
                $"{SecretKey} must be at least {MinimumSecretBytes} bytes ({MinimumSecretBytes * 8} bits) " +
                $"for HMAC-SHA256; the configured value has {secretBytes} bytes.");
        }

        return new JwtSettings(
            secret,
            Require(configuration, IssuerKey, "Jwt__Issuer"),
            Require(configuration, AudienceKey, "Jwt__Audience"));
    }

    public SymmetricSecurityKey CreateSigningKey() => new(Encoding.UTF8.GetBytes(Secret));

    /// <summary>Parâmetros de validação usados pelo middleware de autenticação.</summary>
    public TokenValidationParameters CreateValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = Issuer,
        ValidAudience = Audience,
        IssuerSigningKey = CreateSigningKey(),
        ClockSkew = TimeSpan.Zero
    };

    private static string Require(IConfiguration configuration, string key, string environmentVariable)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{key} is not configured. Set it with `dotnet user-secrets set \"{key}\" \"<value>\"` " +
                $"in Development, or through the `{environmentVariable}` environment variable elsewhere. " +
                "The application does not start without it.");
        }

        return value;
    }
}
