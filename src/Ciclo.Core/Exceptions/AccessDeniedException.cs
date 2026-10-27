namespace Ciclo.Core.Exceptions;

/// <summary>
/// Negativa de acesso a um recurso cujo código é parte do contrato da API (B9).
///
/// Diferente de <see cref="ForbiddenException"/>, que traduz para o código genérico
/// <c>forbidden</c>, esta exceção carrega o código específico — <c>not_linked_to_student</c>,
/// por exemplo. A política de vínculo vive em um só lugar (<c>StudentAccess</c>), mas os
/// serviços têm tipos de exceção próprios; sem esta classe, cada serviço repetiria a regra
/// para preservar o código que o cliente já conhece.
/// </summary>
public class AccessDeniedException : Exception
{
    public int StatusCode { get; } = 403;

    public AccessDeniedException(string code) : base(code) { }
}
