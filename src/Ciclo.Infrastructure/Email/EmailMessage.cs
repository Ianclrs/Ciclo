namespace Ciclo.Infrastructure.Email;

/// <summary>
/// Um e-mail pronto para envio — dado imutável, sem dependência de escopo.
///
/// Carrega tudo o que o envio precisa (destinatário, assunto, corpo) justamente para que o envio
/// possa acontecer **fora** do escopo da requisição: nada de reabrir o <c>AppDbContext</c> depois
/// que a resposta saiu (era o defeito do item C9).
/// </summary>
public sealed record EmailMessage(string ToEmail, string ToName, string Subject, string Body);
