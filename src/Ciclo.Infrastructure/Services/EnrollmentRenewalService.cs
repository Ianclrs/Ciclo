using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Ciclo.Infrastructure.Services;

/// <summary>
/// Rematrícula em lote (Spec 160). Decide <b>quem</b> renovar — prévia, elegibilidade e
/// idempotência — e delega <b>como</b> renovar aos métodos existentes de <see cref="IEnrollmentService"/>.
/// </summary>
public interface IEnrollmentRenewalService
{
    Task<RenewalPreviewDto> GetPreviewAsync(
        Guid periodId, Guid tenantId, UserRole role, CancellationToken cancellationToken = default);

    Task<RenewalBatchResultDto> RenewAsync(
        RenewalBatchRequest request, Guid tenantId, Guid userId, UserRole role,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Orquestrador do lote. Não reescreve nenhuma regra de matrícula: a máquina de estados, a
/// exigência documental, a promoção de <c>Student.AnoLetivo</c> e o evento de auditoria continuam
/// com um dono único em <see cref="EnrollmentService"/> (Spec 160 Design Approach).
/// </summary>
public class EnrollmentRenewalService : IEnrollmentRenewalService
{
    private readonly AppDbContext _db;
    private readonly IEnrollmentService _enrollments;

    public EnrollmentRenewalService(AppDbContext db, IEnrollmentService enrollments)
    {
        _db = db;
        _enrollments = enrollments;
    }

    public async Task<RenewalPreviewDto> GetPreviewAsync(
        Guid periodId, Guid tenantId, UserRole role, CancellationToken cancellationToken = default)
    {
        AccessPolicy.Require(AccessPolicy.Roles.EnrollmentPeriodRead, role);

        var period = await _db.EnrollmentPeriods
            .FirstOrDefaultAsync(p => p.Id == periodId, cancellationToken);

        if (period is null)
            throw new EnrollmentException("period_not_found", 404);

        // Uma consulta para o conjunto inteiro: perguntar por aluno dentro do laço seria uma ida ao
        // banco por linha só para responder "já existe?".
        var existingStudentIds = await _db.Enrollments
            .Where(e => e.EnrollmentPeriodId == period.Id)
            .Select(e => e.StudentId)
            .ToListAsync(cancellationToken);

        // Candidato = aluno cujo ano letivo difere do ano do período destino. Como a aprovação
        // promove o aluno, ele sai da prévia na execução seguinte: a prévia é idempotente por
        // construção, sem coluna de controle.
        var students = await _db.Students
            .Where(s => s.AnoLetivo != period.AnoLetivo)
            .OrderBy(s => s.Nome)
            .ToListAsync(cancellationToken);

        var items = new List<RenewalPreviewItemDto>(students.Count);

        foreach (var student in students)
        {
            var compliance = await DocumentCompliance.EvaluateAsync(_db, student.Id, cancellationToken);
            var validDocumentIds = compliance.ValidDocuments.Select(document => document.Id).ToHashSet();

            // Documentos aprovados INCLUSIVE os vencidos: `IsExpired` é o que separa "reaproveitado"
            // de "venceu, precisa reenviar".
            var carriedDocuments = compliance.ApprovedDocuments
                .Select(document => new CarriedDocumentDto(
                    document.Id,
                    document.DocumentType.Nome,
                    document.DataValidade,
                    IsExpired: !validDocumentIds.Contains(document.Id)))
                .ToList();

            var missingDocumentTypes = compliance.MissingRequiredTypes
                .Select(type => type.Nome)
                .ToList();

            var enrollmentAlreadyExists = existingStudentIds.Contains(student.Id);
            var blockReason = ResolveBlockReason(
                student, enrollmentAlreadyExists, compliance.HasMissingRequiredDocuments);

            items.Add(new RenewalPreviewItemDto(
                student.Id,
                student.Nome,
                Eligible: blockReason is null,
                blockReason,
                carriedDocuments,
                missingDocumentTypes,
                enrollmentAlreadyExists));
        }

        return new RenewalPreviewDto(period.Id, period.Nome, period.AnoLetivo, DateTime.UtcNow, items);
    }

    public async Task<RenewalBatchResultDto> RenewAsync(
        RenewalBatchRequest request, Guid tenantId, Guid userId, UserRole role,
        CancellationToken cancellationToken = default)
    {
        AccessPolicy.Require(AccessPolicy.Roles.EnrollmentWrite, role);

        if (request.StudentIds is null || request.StudentIds.Count == 0)
            throw new EnrollmentException("student_ids_required", 400);

        var period = await _db.EnrollmentPeriods
            .FirstOrDefaultAsync(p => p.Id == request.EnrollmentPeriodId, cancellationToken);

        if (period is null)
            throw new EnrollmentException("period_not_found", 404);

        // Repetição no pedido é pedido do operador, não dois alunos: processa uma vez só.
        var studentIds = request.StudentIds.Distinct().ToList();
        var items = new List<RenewalBatchItemResultDto>(studentIds.Count);

        foreach (var studentId in studentIds)
        {
            var studentName = string.Empty;

            try
            {
                var student = await _db.Students
                    .FirstOrDefaultAsync(s => s.Id == studentId, cancellationToken);

                if (student is null)
                {
                    items.Add(new RenewalBatchItemResultDto(
                        studentId, studentName, RenewalOutcome.Failed, "student_not_found"));
                    continue;
                }

                studentName = student.Nome;

                if (student.Status == StudentStatus.Inativo)
                {
                    items.Add(new RenewalBatchItemResultDto(
                        studentId, studentName, RenewalOutcome.Failed, "student_inactive"));
                    continue;
                }

                if (student.Status == StudentStatus.Transferido)
                {
                    items.Add(new RenewalBatchItemResultDto(
                        studentId, studentName, RenewalOutcome.Failed, "student_transferred"));
                    continue;
                }

                // Chave de idempotência (StudentId, EnrollmentPeriodId): havendo matrícula no
                // período destino, nada é criado e nenhuma transição é executada.
                var alreadyExists = await _db.Enrollments
                    .AnyAsync(
                        e => e.StudentId == studentId && e.EnrollmentPeriodId == period.Id,
                        cancellationToken);

                if (alreadyExists)
                {
                    items.Add(new RenewalBatchItemResultDto(
                        studentId, studentName, RenewalOutcome.SkippedAlreadyExists,
                        "enrollment_already_exists"));
                    continue;
                }

                var compliance = await DocumentCompliance.EvaluateAsync(_db, studentId, cancellationToken);
                if (compliance.HasMissingRequiredDocuments)
                {
                    items.Add(new RenewalBatchItemResultDto(
                        studentId, studentName, RenewalOutcome.BlockedPendingDocuments,
                        "pending_required_documents"));
                    continue;
                }

                // O ciclo de vida é do serviço de matrícula: criar, submeter e aprovar. A promoção de
                // ano letivo e o evento de auditoria acontecem dentro da aprovação (Spec 110).
                var created = await _enrollments.CreateEnrollmentAsync(
                    new CreateEnrollmentRequest(studentId, period.Id), tenantId, role);
                await _enrollments.SubmitAsync(created.Id, tenantId, userId, role);
                await _enrollments.ApproveAsync(created.Id, tenantId, userId, role);

                items.Add(new RenewalBatchItemResultDto(
                    studentId, studentName, RenewalOutcome.Renewed, null));
            }
            catch (EnrollmentException ex)
            {
                // A falha de um aluno não interrompe o lote (FR-003): vira item Failed com o código
                // do erro e o laço segue para o próximo.
                DiscardPendingChanges();
                items.Add(new RenewalBatchItemResultDto(
                    studentId, studentName, RenewalOutcome.Failed, ex.Message));
            }
            catch (Exception ex)
            {
                DiscardPendingChanges();
                items.Add(new RenewalBatchItemResultDto(
                    studentId, studentName, RenewalOutcome.Failed, ex.Message));
            }
        }

        return new RenewalBatchResultDto(
            period.Id,
            period.AnoLetivo,
            items,
            Renewed: items.Count(item => item.Outcome == RenewalOutcome.Renewed),
            Skipped: items.Count(item => item.Outcome == RenewalOutcome.SkippedAlreadyExists),
            Blocked: items.Count(item => item.Outcome == RenewalOutcome.BlockedPendingDocuments),
            Failed: items.Count(item => item.Outcome == RenewalOutcome.Failed));
    }

    /// <summary>
    /// Precedência fixa do FR-001: o primeiro motivo aplicável é o que explica o bloqueio. A ordem
    /// faz parte do contrato — inativo explica mais do que documento pendente.
    /// </summary>
    private static string? ResolveBlockReason(
        Student student, bool enrollmentAlreadyExists, bool hasMissingRequiredDocuments)
    {
        if (student.Status == StudentStatus.Inativo)
            return "student_inactive";

        if (student.Status == StudentStatus.Transferido)
            return "student_transferred";

        if (enrollmentAlreadyExists)
            return "enrollment_already_exists";

        if (hasMissingRequiredDocuments)
            return "pending_required_documents";

        return null;
    }

    /// <summary>
    /// Aluno que falha não pode deixar alteração rastreada para trás. `ApproveAsync` altera o status
    /// e o `Student.AnoLetivo` <b>antes</b> de gravar: se a trilha de auditoria lança, a alteração
    /// fica só na memória — e o `SaveChangesAsync` do próximo aluno a confirmaria sem o evento que
    /// falhou. Tudo o que os alunos anteriores fizeram já foi persistido, então limpar o rastreador
    /// descarta apenas o que este aluno deixou pela metade.
    /// </summary>
    private void DiscardPendingChanges() => _db.ChangeTracker.Clear();
}
