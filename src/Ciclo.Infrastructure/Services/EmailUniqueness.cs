using Ciclo.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Ciclo.Infrastructure.Services;

/// <summary>
/// C2 — o e-mail é a chave de login e, por isso, precisa ser único GLOBALMENTE.
///
/// O login, o "esqueci a senha" e o reset resolvem o usuário apenas pelo e-mail, sem filtrar
/// tenant (<c>IgnoreQueryFilters</c>): quem ainda não se autenticou não tem tenant no token, logo
/// não há outro critério disponível. Se o mesmo e-mail existir em duas escolas, a consulta devolve
/// uma linha arbitrária — a pessoa entra na escola errada, recebe o token de reset de outra conta,
/// ou falha de forma intermitente. Unicidade por tenant (o que o banco garante hoje) não sustenta
/// essa consulta.
///
/// Este é o dono único da regra: os dois caminhos de cadastro (escola e responsável) passam por
/// aqui, e o schema declara o índice único correspondente (ver <c>AppDbContext</c>).
/// </summary>
internal static class EmailUniqueness
{
    /// <summary>Código estável devolvido ao cliente quando o e-mail já está em uso.</summary>
    internal const string ViolationCode = "email_already_registered";

    /// <summary>
    /// Falha com 409 se o e-mail já existir em <b>qualquer</b> tenant.
    /// </summary>
    internal static async Task EnsureAvailableAsync(
        AppDbContext dbContext,
        string email,
        CancellationToken cancellationToken = default)
    {
        var taken = await dbContext.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.Email == email, cancellationToken);

        if (taken)
        {
            throw new AuthException(ViolationCode, 409);
        }
    }
}
