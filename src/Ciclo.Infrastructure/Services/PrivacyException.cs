namespace Ciclo.Infrastructure.Services;

/// <summary>
/// Spec 210 — erro de domínio das operações de privacidade. O código estável viaja em
/// <see cref="Exception.Message"/> e o status HTTP em <see cref="StatusCode"/>, no mesmo
/// formato de <c>StudentException</c>: o controller traduz os dois com
/// <c>ProblemResponse.FromCode</c>. Códigos usados: <c>invalid_request</c> (400),
/// <c>forbidden</c> (403), <c>not_found</c> (404), <c>invalid_state</c> (409),
/// <c>legal_hold_active</c> (409).
/// </summary>
public class PrivacyException : Exception
{
    public int StatusCode { get; }

    public PrivacyException(string code, int statusCode) : base(code) => StatusCode = statusCode;
}

/// <summary>Bloqueio legal ativo: a anonimização é recusada e nada é escrito.</summary>
public sealed class PrivacyLegalHoldException : PrivacyException
{
    public PrivacyLegalHoldException() : base("legal_hold_active", 409) { }
}
