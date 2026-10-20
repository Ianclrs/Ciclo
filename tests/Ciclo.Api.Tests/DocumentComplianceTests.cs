using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Services;
using Ciclo.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ciclo.Api.Tests;

/// <summary>
/// Dados de teste para as regras de documentação (Spec 50 FR-007/FR-008 e Spec 60 FR-004/FR-005).
///
/// Segue o padrão de banco InMemory por teste do projeto, com uma diferença que foi **medida** e
/// importa: o contexto de tenant é um dublê com semântica do real, não um mock com valor fixo.
///
/// O filtro global de tenant é compilado dentro do **modelo** do EF, e o modelo é cacheado pelo tipo
/// do contexto. A expressão prende a instância que criou o modelo
/// (<c>Expression.Constant(_tenantContext)</c> em <c>AppDbContext</c>). Em produção isso é inócuo,
/// porque <c>TenantContext</c> lê um <c>AsyncLocal</c> estático — qualquer instância devolve o
/// tenant do request atual. Um mock, porém, devolve um valor fixo: a partir do segundo contexto no
/// processo o filtro usa o tenant do PRIMEIRO teste e as entidades semeadas "desaparecem", com
/// falhas não determinísticas conforme quem criou o modelo antes.
/// </summary>
internal static class DocumentTestData
{
    /// <summary>
    /// Contexto derivado, e não <c>AppDbContext</c> direto, de propósito: o EF cacheia o modelo por
    /// TIPO de contexto, e o filtro global de tenant fica compilado nele, preso à instância que
    /// construiu o modelo. Como outras classes de teste criam <c>AppDbContext</c> com dublês, quem
    /// construir o modelo primeiro define o tenant de todo mundo — os testes aqui passariam a
    /// filtrar por um tenant alheio e "não encontrariam" o que semearam. Com tipo próprio, este
    /// arquivo tem o seu modelo, construído pela instância ambiente (que lê o tenant do fluxo).
    /// </summary>
    private sealed class DocumentTestDbContext : AppDbContext
    {
        internal DocumentTestDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
            : base(options, tenantContext) { }
    }

    /// <summary>Tenant fixo, com a mesma superfície do real, sem estado compartilhado.</summary>
    private sealed class FixedTenantContext : ITenantContext
    {
        private readonly Guid _tenantId;

        internal FixedTenantContext(Guid tenantId) => _tenantId = tenantId;

        public Guid TenantId => _tenantId;

        public bool IsResolved => true;

        public void SetTenant(Guid tenantId) =>
            throw new NotSupportedException("O tenant é fixo no contexto de teste.");
    }

    /// <summary>
    /// Tenant único deste arquivo de teste. O isolamento entre testes vem do nome do banco
    /// InMemory (único por teste), não do tenant — então um valor fixo basta e elimina qualquer
    /// dependência de estado ambiente (AsyncLocal), que se mostrou frágil aqui.
    /// </summary>
    private static readonly Guid TestTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    internal static (AppDbContext Db, Guid TenantId) CreateContext()
    {
        var tenantId = Guid.NewGuid();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        // Um tenant fixo para o arquivo inteiro, e não um valor por teste: o filtro de tenant é
        // compilado no modelo do EF e fica preso à instância que o criou. Isolar por banco InMemory
        // (nome único por teste) é o que separa os testes; variar o tenant exigiria um modelo por
        // teste e não traria proteção adicional.

        return (new DocumentTestDbContext(options, new FixedTenantContext(TestTenantId)), TestTenantId);
    }


    internal static async Task<DocumentType> AddDocumentTypeAsync(
        AppDbContext db, Guid tenantId, string nome, bool isRequired = true, bool isActive = true)
    {
        var type = new DocumentType
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Nome = nome,
            IsRequired = isRequired,
            IsActive = isActive
        };
        db.DocumentTypes.Add(type);
        await db.SaveChangesAsync();
        return type;
    }

    internal static async Task<Document> AddDocumentAsync(
        AppDbContext db, Guid tenantId, Guid studentId, Guid documentTypeId,
        DocumentStatus status, DateTime? dataValidade = null)
    {
        var document = new Document
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            StudentId = studentId,
            DocumentTypeId = documentTypeId,
            NomeArquivo = "arquivo.pdf",
            CaminhoArquivo = "/tmp/arquivo.pdf",
            Status = status,
            DataValidade = dataValidade,
            CreatedAt = DateTime.UtcNow
        };
        db.Documents.Add(document);
        await db.SaveChangesAsync();
        return document;
    }

    internal static async Task<Student> AddStudentAsync(
        AppDbContext db, Guid tenantId, StudentStatus status = StudentStatus.Ativo)
    {
        var student = new Student
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Nome = "Aluno Teste",
            DataNascimento = new DateTime(2015, 5, 20, 0, 0, 0, DateTimeKind.Utc),
            Turma = "5A",
            AnoLetivo = 2025,
            Status = status,
            CreatedAt = DateTime.UtcNow
        };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        return student;
    }

    /// <summary>Período ativo e dentro da janela de datas (o serviço de rematrícula exige os dois).</summary>
    internal static async Task<EnrollmentPeriod> AddPeriodAsync(
        AppDbContext db, Guid tenantId, int anoLetivo, bool isActive = true)
    {
        var period = new EnrollmentPeriod
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Nome = $"Rematrícula {anoLetivo}",
            DataInicio = DateTime.UtcNow.AddDays(-1),
            DataFim = DateTime.UtcNow.AddDays(30),
            AnoLetivo = anoLetivo,
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow
        };
        db.EnrollmentPeriods.Add(period);
        await db.SaveChangesAsync();
        return period;
    }

    internal static async Task<Enrollment> AddEnrollmentAsync(
        AppDbContext db, Guid tenantId, Guid studentId, Guid periodId, EnrollmentStatus status)
    {
        var enrollment = new Enrollment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            StudentId = studentId,
            EnrollmentPeriodId = periodId,
            Status = status,
            CreatedAt = DateTime.UtcNow
        };
        db.Enrollments.Add(enrollment);
        await db.SaveChangesAsync();
        return enrollment;
    }
}

/// <summary>
/// A2 — Spec 50 FR-007 (submit) e FR-008 (aprovação).
///
/// Antes desta correção o submit sempre ia para <c>Pendente</c> e a aprovação nunca checava
/// documento: o estado <c>DocumentacaoPendente</c> existia na máquina de estados mas era
/// inalcançável em produção (só o seeder o criava).
/// </summary>
public sealed class EnrollmentDocumentGatingTests
{
    [Fact]
    public async Task SubmitAsync_WithoutRequiredDocuments_GoesToDocumentacaoPendente()
    {
        var (db, tenantId) = DocumentTestData.CreateContext();
        using var _ = db;
        await DocumentTestData.AddDocumentTypeAsync(db, tenantId, "Certidão de nascimento");

        var student = await DocumentTestData.AddStudentAsync(db, tenantId);
        var period = await DocumentTestData.AddPeriodAsync(db, tenantId, 2026);
        var enrollment = await DocumentTestData.AddEnrollmentAsync(
            db, tenantId, student.Id, period.Id, EnrollmentStatus.Rascunho);

        var result = await new EnrollmentService(db).SubmitAsync(enrollment.Id, tenantId);

        Assert.Equal(EnrollmentStatus.DocumentacaoPendente.ToString(), result.Status);
    }

    /// <summary>Controle positivo: com a documentação em ordem o destino é Pendente.</summary>
    [Fact]
    public async Task SubmitAsync_WithAllRequiredDocumentsValid_GoesToPendente()
    {
        var (db, tenantId) = DocumentTestData.CreateContext();
        using var _ = db;
        var type = await DocumentTestData.AddDocumentTypeAsync(db, tenantId, "Certidão de nascimento");

        var student = await DocumentTestData.AddStudentAsync(db, tenantId);
        await DocumentTestData.AddDocumentAsync(db, tenantId, student.Id, type.Id, DocumentStatus.Aprovado);
        var period = await DocumentTestData.AddPeriodAsync(db, tenantId, 2026);
        var enrollment = await DocumentTestData.AddEnrollmentAsync(
            db, tenantId, student.Id, period.Id, EnrollmentStatus.Rascunho);

        var result = await new EnrollmentService(db).SubmitAsync(enrollment.Id, tenantId);

        Assert.Equal(EnrollmentStatus.Pendente.ToString(), result.Status);
    }

    /// <summary>
    /// O detalhe que separa "tem o documento" de "tem documento válido": um documento aprovado mas
    /// vencido não conta como entregue, então a matrícula fica pendente.
    /// </summary>
    [Fact]
    public async Task SubmitAsync_WithExpiredDocument_GoesToDocumentacaoPendente()
    {
        var (db, tenantId) = DocumentTestData.CreateContext();
        using var _ = db;
        var type = await DocumentTestData.AddDocumentTypeAsync(db, tenantId, "Atestado médico");

        var student = await DocumentTestData.AddStudentAsync(db, tenantId);
        await DocumentTestData.AddDocumentAsync(db, tenantId, student.Id, type.Id,
            DocumentStatus.Aprovado, dataValidade: DateTime.UtcNow.AddDays(-1));
        var period = await DocumentTestData.AddPeriodAsync(db, tenantId, 2026);
        var enrollment = await DocumentTestData.AddEnrollmentAsync(
            db, tenantId, student.Id, period.Id, EnrollmentStatus.Rascunho);

        var result = await new EnrollmentService(db).SubmitAsync(enrollment.Id, tenantId);

        Assert.Equal(EnrollmentStatus.DocumentacaoPendente.ToString(), result.Status);
    }

    /// <summary>
    /// Spec 60 E5: sem nenhum tipo obrigatório configurado não há pendência — a escola que não
    /// cadastrou exigências não pode ficar com toda matrícula travada.
    /// </summary>
    [Fact]
    public async Task SubmitAsync_WithNoRequiredTypesConfigured_GoesToPendente()
    {
        var (db, tenantId) = DocumentTestData.CreateContext();
        using var _ = db;
        await DocumentTestData.AddDocumentTypeAsync(db, tenantId, "Opcional", isRequired: false);

        var student = await DocumentTestData.AddStudentAsync(db, tenantId);
        var period = await DocumentTestData.AddPeriodAsync(db, tenantId, 2026);
        var enrollment = await DocumentTestData.AddEnrollmentAsync(
            db, tenantId, student.Id, period.Id, EnrollmentStatus.Rascunho);

        var result = await new EnrollmentService(db).SubmitAsync(enrollment.Id, tenantId);

        Assert.Equal(EnrollmentStatus.Pendente.ToString(), result.Status);
    }

    [Fact]
    public async Task ApproveAsync_WithoutRequiredDocuments_IsRejectedWith400()
    {
        var (db, tenantId) = DocumentTestData.CreateContext();
        using var _ = db;
        await DocumentTestData.AddDocumentTypeAsync(db, tenantId, "Certidão de nascimento");

        var student = await DocumentTestData.AddStudentAsync(db, tenantId);
        var period = await DocumentTestData.AddPeriodAsync(db, tenantId, 2026);
        var enrollment = await DocumentTestData.AddEnrollmentAsync(
            db, tenantId, student.Id, period.Id, EnrollmentStatus.DocumentacaoPendente);

        var exception = await Assert.ThrowsAsync<EnrollmentException>(
            () => new EnrollmentService(db).ApproveAsync(enrollment.Id, tenantId));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("pending_required_documents", exception.Message);
    }

    /// <summary>Controle positivo: com tudo em ordem a aprovação passa e atualiza o aluno.</summary>
    [Fact]
    public async Task ApproveAsync_WithAllRequiredDocumentsValid_ApprovesAndUpdatesStudent()
    {
        var (db, tenantId) = DocumentTestData.CreateContext();
        using var _ = db;
        var type = await DocumentTestData.AddDocumentTypeAsync(db, tenantId, "Certidão de nascimento");

        var student = await DocumentTestData.AddStudentAsync(db, tenantId);
        await DocumentTestData.AddDocumentAsync(db, tenantId, student.Id, type.Id, DocumentStatus.Aprovado);
        var period = await DocumentTestData.AddPeriodAsync(db, tenantId, 2026);
        var enrollment = await DocumentTestData.AddEnrollmentAsync(
            db, tenantId, student.Id, period.Id, EnrollmentStatus.Pendente);

        var result = await new EnrollmentService(db).ApproveAsync(enrollment.Id, tenantId);

        Assert.Equal(EnrollmentStatus.Aprovado.ToString(), result.Status);

        var updated = await db.Students.IgnoreQueryFilters().SingleAsync(s => s.Id == student.Id);
        Assert.Equal(StudentStatus.Ativo, updated.Status);
        Assert.Equal(2026, updated.AnoLetivo);
    }

    /// <summary>
    /// Um tipo obrigatório <b>desativado</b> deixa de ser exigência: senão a escola não conseguiria
    /// parar de cobrar um documento antigo.
    /// </summary>
    [Fact]
    public async Task SubmitAsync_IgnoresRequiredTypeThatIsInactive()
    {
        var (db, tenantId) = DocumentTestData.CreateContext();
        using var _ = db;
        await DocumentTestData.AddDocumentTypeAsync(db, tenantId, "Documento aposentado", isActive: false);

        var student = await DocumentTestData.AddStudentAsync(db, tenantId);
        var period = await DocumentTestData.AddPeriodAsync(db, tenantId, 2026);
        var enrollment = await DocumentTestData.AddEnrollmentAsync(
            db, tenantId, student.Id, period.Id, EnrollmentStatus.Rascunho);

        var result = await new EnrollmentService(db).SubmitAsync(enrollment.Id, tenantId);

        Assert.Equal(EnrollmentStatus.Pendente.ToString(), result.Status);
    }
}

/// <summary>
/// A4 — a visão de documentação no detalhe da matrícula (Spec 60 FR-004).
///
/// Vivía na API separada de rematrícula, removida no A4 por ser uma segunda superfície sobre a
/// MESMA tabela (mesmos create/list/get/approve/reject de `/api/enrollments`). Manter as duas
/// custou caro na prática: a correção do A3 teve de ser aplicada duas vezes, uma em cada serviço.
/// </summary>
public sealed class EnrollmentDetailDocumentTests
{
    private static readonly string[] ExpectedMissingTypes =
        ["Atestado médico", "Comprovante de residência"];

    [Fact]
    public async Task Detail_ReportsCarriedDocumentsAndMissingTypes()
    {
        var (db, tenantId) = DocumentTestData.CreateContext();
        using var _ = db;

        var carriedType = await DocumentTestData.AddDocumentTypeAsync(db, tenantId, "Certidão de nascimento");
        var expiredType = await DocumentTestData.AddDocumentTypeAsync(db, tenantId, "Atestado médico");
        await DocumentTestData.AddDocumentTypeAsync(db, tenantId, "Comprovante de residência");

        var student = await DocumentTestData.AddStudentAsync(db, tenantId);
        await DocumentTestData.AddDocumentAsync(db, tenantId, student.Id, carriedType.Id,
            DocumentStatus.Aprovado, dataValidade: DateTime.UtcNow.AddMonths(6));
        await DocumentTestData.AddDocumentAsync(db, tenantId, student.Id, expiredType.Id,
            DocumentStatus.Aprovado, dataValidade: DateTime.UtcNow.AddDays(-5));

        var period = await DocumentTestData.AddPeriodAsync(db, tenantId, 2026);
        var enrollment = await DocumentTestData.AddEnrollmentAsync(
            db, tenantId, student.Id, period.Id, EnrollmentStatus.Pendente);

        var detail = await new EnrollmentService(db)
            .GetEnrollmentByIdAsync(enrollment.Id, tenantId, Guid.NewGuid(), UserRole.Admin);

        Assert.NotNull(detail.CarriedDocuments);
        Assert.Equal(2, detail.CarriedDocuments!.Count);
        Assert.Contains(detail.CarriedDocuments, d => d.DocumentType == "Certidão de nascimento" && !d.IsExpired);
        Assert.Contains(detail.CarriedDocuments, d => d.DocumentType == "Atestado médico" && d.IsExpired);
        Assert.Equal(ExpectedMissingTypes, detail.MissingDocumentTypes!.OrderBy(n => n, StringComparer.Ordinal));
    }

    /// <summary>
    /// O detalhe e a aprovação têm de contar a MESMA história: se o detalhe diz que não falta
    /// nada, aprovar precisa funcionar; se diz que falta, aprovar precisa ser barrado. Sem isso, a
    /// tela poderia exibir "em dia" e o botão falhar.
    /// </summary>
    [Fact]
    public async Task Detail_AgreesWithTheApproveGate()
    {
        var (db, tenantId) = DocumentTestData.CreateContext();
        using var _ = db;
        await DocumentTestData.AddDocumentTypeAsync(db, tenantId, "Certidão de nascimento");

        var student = await DocumentTestData.AddStudentAsync(db, tenantId);
        var period = await DocumentTestData.AddPeriodAsync(db, tenantId, 2026);
        var enrollment = await DocumentTestData.AddEnrollmentAsync(
            db, tenantId, student.Id, period.Id, EnrollmentStatus.Pendente);

        var service = new EnrollmentService(db);
        var detail = await service.GetEnrollmentByIdAsync(enrollment.Id, tenantId, Guid.NewGuid(), UserRole.Admin);

        Assert.NotEmpty(detail.MissingDocumentTypes!);
        await Assert.ThrowsAsync<EnrollmentException>(() => service.ApproveAsync(enrollment.Id, tenantId));
    }
}
