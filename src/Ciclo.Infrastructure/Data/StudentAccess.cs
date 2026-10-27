using Microsoft.EntityFrameworkCore;
using Ciclo.Core.Entities;
using Ciclo.Core.Exceptions;

namespace Ciclo.Infrastructure.Data;

/// <summary>
/// Dono único da regra "o responsável só vê o aluno ao qual está vinculado" (B9).
///
/// Antes, a mesma regra aparecia em ~10 pontos: quatro serviços repetiam
/// <c>if (role == UserRole.Parent)</c> e consultavam <c>StudentParents</c> por conta própria, cada
/// um lançando o seu tipo de exceção. O código visível ao cliente também divergia: alguns
/// caminhos devolviam <c>not_linked_to_student</c> e outros <c>forbidden</c> para a MESMA condição.
///
/// Aqui a regra tem duas formas, porque o uso é diferente:
/// <list type="bullet">
/// <item><see cref="VisibleTo(IQueryable{Student},Guid,UserRole)"/> e a sobrecarga de
/// <see cref="Enrollment"/> restringem uma CONSULTA (listagens) sem consulta extra;</item>
/// <item><see cref="EnsureLinkedAsync"/> protege um RECURSO já identificado (detalhe, download,
/// upload), incluindo a checagem de que o aluno existe neste tenant.</item>
/// </list>
/// </summary>
public static class StudentAccess
{
    /// <summary>Restringe a listagem de alunos aos vinculados ao responsável. Outros papéis veem tudo.</summary>
    public static IQueryable<Student> VisibleTo(this IQueryable<Student> query, Guid userId, UserRole role) =>
        role == UserRole.Parent
            ? query.Where(s => s.StudentParents.Any(sp => sp.ParentId == userId))
            : query;

    /// <summary>Restringe a listagem de matrículas às dos filhos do responsável.</summary>
    public static IQueryable<Enrollment> VisibleTo(this IQueryable<Enrollment> query, Guid userId, UserRole role) =>
        role == UserRole.Parent
            ? query.Where(e => e.Student.StudentParents.Any(sp => sp.ParentId == userId))
            : query;

    /// <summary>
    /// Exige que o aluno exista neste tenant e, para o papel Parent, que esteja vinculado.
    /// O 403 é devolvido também quando o aluno não existe: distinguir 404 de 403 permitiria a um
    /// responsável descobrir quais identificadores existem em outro tenant.
    /// </summary>
    public static async Task EnsureLinkedAsync(AppDbContext db, Guid studentId, Guid userId, UserRole role)
    {
        // Para outros papéis a existência é responsabilidade do chamador (que devolve 404 com o
        // código do próprio recurso). Aqui só o responsável precisa da checagem combinada.
        if (role != UserRole.Parent)
            return;

        // O filtro global de tenant já restringe estes EXISTS ao tenant corrente.
        var studentExists = await db.Students.AnyAsync(s => s.Id == studentId);
        if (!studentExists)
            throw new AccessDeniedException("not_linked_to_student");

        var isLinked = await db.StudentParents
            .AnyAsync(sp => sp.StudentId == studentId && sp.ParentId == userId);

        if (!isLinked)
            throw new AccessDeniedException("not_linked_to_student");
    }
}
