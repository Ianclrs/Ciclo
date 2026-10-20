using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Ciclo.Infrastructure.Services;

/// <summary>
/// Situação documental de um aluno perante as exigências da escola.
/// </summary>
/// <param name="ApprovedDocuments">
/// Todos os documentos aprovados do aluno, <b>inclusive os vencidos</b>. Existe para a tela da
/// rematrícula poder distinguir "reaproveitado" de "venceu — precisa reenviar" (o
/// <c>CarriedDocumentDto.IsExpired</c> depende disso, e V6 pede notificação de documento vencido).
/// </param>
/// <param name="ValidDocuments">
/// Documentos que contam como entregues: aprovados e não vencidos. É a lista que a rematrícula
/// reaproveita (Spec 60 FR-004) e a base do cálculo de pendência.
/// </param>
/// <param name="MissingRequiredTypes">
/// Tipos que a escola exige hoje e para os quais o aluno <b>não</b> tem documento válido.
/// </param>
internal sealed record StudentDocumentCompliance(
    IReadOnlyList<Document> ApprovedDocuments,
    IReadOnlyList<Document> ValidDocuments,
    IReadOnlyList<DocumentType> MissingRequiredTypes)
{
    internal bool HasMissingRequiredDocuments => MissingRequiredTypes.Count > 0;
}

/// <summary>
/// Dono único da pergunta "a documentação deste aluno está em dia?" — Spec 50 FR-007/FR-008 e
/// Spec 60 FR-004/FR-005.
///
/// Existe porque a mesma regra é necessária em cinco pontos de dois serviços (submit e aprovação de
/// matrícula; criação, consulta e aprovação de rematrícula). Antes, cada ponto tinha um TODO e a
/// regra não existia em lugar nenhum — o estado <c>DocumentacaoPendente</c> estava na máquina de
/// estados mas era <b>inalcançável</b> em produção (só o seeder o criava).
///
/// Pressupõe escopo de tenant resolvido: as consultas contam com o filtro global de tenant
/// (ADR-002) e por isso não recebem <c>tenantId</c> — passá-lo seria uma segunda checagem da mesma
/// coisa. Fora de um escopo resolvido, o próprio <c>AppDbContext</c> falha (fail-closed).
/// </summary>
internal static class DocumentCompliance
{
    internal static async Task<StudentDocumentCompliance> EvaluateAsync(
        AppDbContext dbContext,
        Guid studentId,
        CancellationToken cancellationToken = default)
    {
        // Uma consulta só, particionada em memória: o critério de "aprovado" é o mesmo, e o
        // resultado alimenta tanto a regra (validDocuments) quanto a tela (approvedDocuments).
        var approvedDocuments = await dbContext.Documents
            .Include(document => document.DocumentType)
            .Where(document => document.StudentId == studentId
                && document.Status == DocumentStatus.Aprovado)
            .ToListAsync(cancellationToken);

        // "Válido" = aprovado E não vencido. Validade nula significa "não expira".
        var validDocuments = approvedDocuments
            .Where(document => document.DataValidade == null || document.DataValidade > DateTime.UtcNow)
            .ToList();

        // Só tipos ativos: um tipo desativado pela escola deixa de ser exigência.
        var requiredTypes = await dbContext.DocumentTypes
            .Where(type => type.IsRequired && type.IsActive)
            .ToListAsync(cancellationToken);

        // Sem nenhum tipo obrigatório configurado, não há pendência (Spec 60 E5): a lista fica
        // vazia e o chamador segue para Pendente — não para DocumentacaoPendente.
        var missingRequiredTypes = requiredTypes
            .Where(type => validDocuments.All(document => document.DocumentTypeId != type.Id))
            .ToList();

        return new StudentDocumentCompliance(approvedDocuments, validDocuments, missingRequiredTypes);
    }
}
