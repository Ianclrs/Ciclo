using Ciclo.Core.Entities;
using Ciclo.Core.Notifications;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Email;
using Ciclo.Infrastructure.Notifications;
using Ciclo.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ciclo.Api.Tests;

/// <summary>
/// Spec 140 — comunicação proativa.
///
/// O que importa aqui é o estado persistido: quais notificações in-app existem, quais linhas de
/// <see cref="NotificationDelivery"/> foram gravadas (uma por gatilho, destinatário e canal), com que
/// desfecho, e quantos e-mails saíram pelo <see cref="IEmailSender"/> (dublê, sem rede).
///
/// Nada depende do relógio real: as janelas do caminho agendado são decididas pelo <c>nowUtc</c>
/// passado explicitamente, então o mesmo dado produz sempre o mesmo resultado.
///
/// Sobre o filtro de tenant: ele é compilado no modelo do EF e fica preso à instância de
/// <see cref="ITenantContext"/> que construiu o modelo (o mesmo motivo medido em
/// <c>DocumentComplianceTests</c>). Por isso este arquivo tem tipo de contexto próprio e uma única
/// instância mutável compartilhada — o job troca o tenant no meio da execução, e o filtro precisa
/// enxergar a troca.
/// </summary>
public sealed class ProactiveNotificationTests
{
    private static readonly DateTime Now = new(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc);

    private static readonly Guid TestTenantId = Guid.Parse("14014014-0000-0000-0000-000000000001");

    private static readonly MutableTenantContext Ambient = new();

    private static readonly NotificationTriggerKind[] EventKinds =
    [
        NotificationTriggerKind.MatriculaSubmetida,
        NotificationTriggerKind.MatriculaAprovada,
        NotificationTriggerKind.MatriculaRejeitada,
        NotificationTriggerKind.DocumentoVerificado,
        NotificationTriggerKind.DocumentoRejeitado
    ];

    // ── FR-001: gatilho por evento ───────────────────────────────────

    /// <summary>
    /// Os cinco eventos de domínio, dois responsáveis: cada evento deixa uma notificação in-app e um
    /// e-mail por responsável, e nenhum texto guardado leva placeholder não substituído.
    /// </summary>
    [Fact]
    public async Task HandleAsync_ForEachEventKind_CreatesOneInAppNotificationPerParent()
    {
        using var db = CreateDbContext();
        var (student, parents) = await SeedStudentAsync(
            db, TestTenantId, "Alice Souza",
            ("Pai A", "pai-a@test.com"),
            ("Pai B", "pai-b@test.com"));

        var sender = new RecordingEmailSender();
        var service = CreateService(db, sender);

        foreach (var kind in EventKinds)
        {
            await service.HandleAsync(
                new NotificationTriggerEvent(
                    kind, TestTenantId, student.Id, Guid.NewGuid(), DateTime.UtcNow),
                CancellationToken.None);
        }

        var notifications = await db.Notifications.IgnoreQueryFilters().ToListAsync();
        var userNotifications = await db.UserNotifications.ToListAsync();

        Assert.Equal(EventKinds.Length * parents.Count, notifications.Count);
        Assert.Equal(EventKinds.Length * parents.Count, userNotifications.Count);
        Assert.Equal(EventKinds.Length * parents.Count, sender.Sent.Count);

        foreach (var parent in parents)
        {
            Assert.Equal(EventKinds.Length, userNotifications.Count(row => row.UserId == parent.Id));
        }

        Assert.All(notifications, notification =>
            Assert.DoesNotContain("{aluno}", notification.Mensagem, StringComparison.Ordinal));
        Assert.All(notifications, notification =>
            Assert.Contains(student.Nome, notification.Mensagem, StringComparison.Ordinal));
    }

    /// <summary>
    /// Responsável sem endereço recebe só o canal in-app: a ausência do e-mail não é falha de entrega
    /// e por isso não cria linha de e-mail.
    /// </summary>
    [Fact]
    public async Task HandleAsync_ForParentWithoutEmail_CreatesOnlyTheInAppChannel()
    {
        using var db = CreateDbContext();
        var (student, _) = await SeedStudentAsync(
            db, TestTenantId, "Alice Souza", ("Pai sem e-mail", null));

        var sender = new RecordingEmailSender();
        var service = CreateService(db, sender);

        await service.HandleAsync(
            new NotificationTriggerEvent(
                NotificationTriggerKind.MatriculaAprovada, TestTenantId, student.Id, Guid.NewGuid(),
                DateTime.UtcNow),
            CancellationToken.None);

        var delivery = Assert.Single(await db.NotificationDeliveries.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(NotificationDeliveryChannel.InApp, delivery.Channel);
        Assert.Equal(NotificationDeliveryState.Enviado, delivery.State);
        Assert.Equal(TestTenantId, delivery.TenantId);

        Assert.Single(await db.Notifications.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(sender.Sent);
    }

    /// <summary>Aluno inexistente no tenant: nada é escrito e nada lança — a ausência vira aviso.</summary>
    [Fact]
    public async Task HandleAsync_ForUnknownStudent_DoesNotThrowAndWritesNothing()
    {
        using var db = CreateDbContext();
        var sender = new RecordingEmailSender();
        var service = CreateService(db, sender);

        await service.HandleAsync(
            new NotificationTriggerEvent(
                NotificationTriggerKind.DocumentoVerificado, TestTenantId, Guid.NewGuid(), Guid.NewGuid(),
                DateTime.UtcNow),
            CancellationToken.None);

        Assert.Empty(await db.NotificationDeliveries.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.Notifications.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(sender.Sent);
    }

    // ── FR-005: idempotência ─────────────────────────────────────────

    /// <summary>
    /// O mesmo evento duas vezes entrega uma só vez por canal e não altera a linha já gravada: a
    /// segunda chamada é suprimida, não repetida.
    /// </summary>
    [Fact]
    public async Task HandleAsync_CalledTwice_SendsOnlyOnce()
    {
        using var db = CreateDbContext();
        var (student, _) = await SeedStudentAsync(
            db, TestTenantId, "Alice Souza", ("Pai A", "pai-a@test.com"));

        var sender = new RecordingEmailSender();
        var service = CreateService(db, sender);

        var trigger = new NotificationTriggerEvent(
            NotificationTriggerKind.MatriculaAprovada, TestTenantId, student.Id, Guid.NewGuid(),
            DateTime.UtcNow);

        await service.HandleAsync(trigger, CancellationToken.None);
        var primeira = await db.NotificationDeliveries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .ToListAsync();

        await service.HandleAsync(trigger, CancellationToken.None);
        var depois = await db.NotificationDeliveries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .ToListAsync();

        // Uma linha por canal depois da primeira entrega; a segunda não acrescenta nada.
        Assert.Equal(2, primeira.Count);
        Assert.Equal(
            primeira.Select(delivery => delivery.Id).OrderBy(id => id),
            depois.Select(delivery => delivery.Id).OrderBy(id => id));

        Assert.Single(sender.Sent);
        Assert.Single(await db.Notifications.IgnoreQueryFilters().ToListAsync());
        Assert.All(depois, delivery => Assert.Equal(NotificationDeliveryState.Enviado, delivery.State));
    }

    // ── FR-006: registro de entrega ──────────────────────────────────

    /// <summary>
    /// Um e-mail recusado é registrado como <c>Falhou</c> com o texto do erro e não leva junto o
    /// in-app dos demais destinatários.
    /// </summary>
    [Fact]
    public async Task HandleAsync_WhenEmailSenderThrows_RecordsFailedAndKeepsOtherRecipients()
    {
        using var db = CreateDbContext();
        var (student, parents) = await SeedStudentAsync(
            db, TestTenantId, "Alice Souza",
            ("Pai A", "pai-a@test.com"),
            ("Pai B", "falha@test.com"),
            ("Pai C", "pai-c@test.com"));

        var sender = new RecordingEmailSender("falha@test.com");
        var service = CreateService(db, sender);

        await service.HandleAsync(
            new NotificationTriggerEvent(
                NotificationTriggerKind.MatriculaAprovada, TestTenantId, student.Id, Guid.NewGuid(),
                DateTime.UtcNow),
            CancellationToken.None);

        var deliveries = await db.NotificationDeliveries.IgnoreQueryFilters().ToListAsync();

        var inApp = deliveries
            .Where(delivery => delivery.Channel == NotificationDeliveryChannel.InApp)
            .ToList();
        Assert.Equal(parents.Count, inApp.Count);
        Assert.All(inApp, delivery => Assert.Equal(NotificationDeliveryState.Enviado, delivery.State));

        var email = deliveries
            .Where(delivery => delivery.Channel == NotificationDeliveryChannel.Email)
            .ToList();
        Assert.Equal(parents.Count, email.Count);
        Assert.Equal(2, email.Count(delivery => delivery.State == NotificationDeliveryState.Enviado));

        var failed = Assert.Single(
            email, delivery => delivery.State == NotificationDeliveryState.Falhou);
        var erro = Assert.IsType<string>(failed.Erro);
        Assert.Contains("smtp_recusado", erro, StringComparison.Ordinal);

        // Os três responsáveis continuam com a notificação in-app gravada.
        Assert.Equal(parents.Count, (await db.Notifications.IgnoreQueryFilters().ToListAsync()).Count);
    }

    // ── FR-002: gatilho por agendamento ──────────────────────────────

    /// <summary>Documento aprovado vencendo dentro da janela de 30 dias produz um gatilho.</summary>
    [Fact]
    public async Task RunScheduledAsync_DocumentInsideWindow_EmitsOneTrigger()
    {
        using var db = CreateDbContext();
        await SeedTenantAsync(db, TestTenantId, "escola-teste");
        var (student, _) = await SeedStudentAsync(
            db, TestTenantId, "Alice Souza", ("Pai A", "pai-a@test.com"));
        await SeedDocumentAsync(
            db, TestTenantId, student.Id, DocumentStatus.Aprovado, Now.AddDays(10));

        var service = CreateService(db, new RecordingEmailSender());

        var emitidos = await service.RunScheduledAsync(Now, CancellationToken.None);

        Assert.Equal(1, emitidos);

        var deliveries = await db.NotificationDeliveries.IgnoreQueryFilters().ToListAsync();
        Assert.Equal(2, deliveries.Count);
        Assert.All(deliveries, delivery =>
            Assert.Equal(NotificationTriggerKind.DocumentoAVencer, delivery.Kind));
        Assert.All(deliveries, delivery =>
            Assert.Equal(NotificationDeliveryState.Enviado, delivery.State));
    }

    /// <summary>O limite superior da janela é inclusivo: exatamente 30 dias ainda vence.</summary>
    [Fact]
    public async Task RunScheduledAsync_DocumentAtThirtyDayBoundary_EmitsOneTrigger()
    {
        using var db = CreateDbContext();
        await SeedTenantAsync(db, TestTenantId, "escola-teste");
        var (student, _) = await SeedStudentAsync(
            db, TestTenantId, "Alice Souza", ("Pai A", "pai-a@test.com"));
        await SeedDocumentAsync(
            db, TestTenantId, student.Id, DocumentStatus.Aprovado, Now.AddDays(30));

        var service = CreateService(db, new RecordingEmailSender());

        Assert.Equal(1, await service.RunScheduledAsync(Now, CancellationToken.None));
    }

    /// <summary>31 dias está fora da janela: nenhum gatilho.</summary>
    [Fact]
    public async Task RunScheduledAsync_DocumentOutsideWindow_EmitsNone()
    {
        using var db = CreateDbContext();
        await SeedTenantAsync(db, TestTenantId, "escola-teste");
        var (student, _) = await SeedStudentAsync(
            db, TestTenantId, "Alice Souza", ("Pai A", "pai-a@test.com"));
        await SeedDocumentAsync(
            db, TestTenantId, student.Id, DocumentStatus.Aprovado, Now.AddDays(31));

        var service = CreateService(db, new RecordingEmailSender());

        Assert.Equal(0, await service.RunScheduledAsync(Now, CancellationToken.None));
        Assert.Empty(await db.NotificationDeliveries.IgnoreQueryFilters().ToListAsync());
    }

    /// <summary>Documento já vencido não é candidato: esta spec não notifica vencimento passado.</summary>
    [Fact]
    public async Task RunScheduledAsync_ExpiredDocument_EmitsNone()
    {
        using var db = CreateDbContext();
        await SeedTenantAsync(db, TestTenantId, "escola-teste");
        var (student, _) = await SeedStudentAsync(
            db, TestTenantId, "Alice Souza", ("Pai A", "pai-a@test.com"));
        await SeedDocumentAsync(
            db, TestTenantId, student.Id, DocumentStatus.Aprovado, Now.AddDays(-1));

        var service = CreateService(db, new RecordingEmailSender());

        Assert.Equal(0, await service.RunScheduledAsync(Now, CancellationToken.None));
        Assert.Empty(await db.NotificationDeliveries.IgnoreQueryFilters().ToListAsync());
    }

    /// <summary>
    /// Período ativo abrindo na data do ciclo e com prazo dentro de 7 dias: um gatilho de abertura e
    /// um de prazo final, cada um com a entrega in-app e de e-mail do responsável.
    /// </summary>
    [Fact]
    public async Task RunScheduledAsync_ActivePeriod_EmitsOpeningAndDeadline()
    {
        using var db = CreateDbContext();
        await SeedTenantAsync(db, TestTenantId, "escola-teste");
        var (student, _) = await SeedStudentAsync(
            db, TestTenantId, "Alice Souza", ("Pai A", "pai-a@test.com"));
        await SeedPeriodAsync(db, TestTenantId, student.Id, Now.Date, Now.AddDays(3), isActive: true);

        var service = CreateService(db, new RecordingEmailSender());

        var emitidos = await service.RunScheduledAsync(Now, CancellationToken.None);

        Assert.Equal(2, emitidos);

        var deliveries = await db.NotificationDeliveries.IgnoreQueryFilters().ToListAsync();
        Assert.Equal(2, deliveries.Count(delivery =>
            delivery.Kind == NotificationTriggerKind.RematriculaAberta));
        Assert.Equal(2, deliveries.Count(delivery =>
            delivery.Kind == NotificationTriggerKind.RematriculaPrazoFinal));
    }

    /// <summary>Período inativo não abre nem entra em prazo final.</summary>
    [Fact]
    public async Task RunScheduledAsync_InactivePeriod_EmitsNone()
    {
        using var db = CreateDbContext();
        await SeedTenantAsync(db, TestTenantId, "escola-teste");
        var (student, _) = await SeedStudentAsync(
            db, TestTenantId, "Alice Souza", ("Pai A", "pai-a@test.com"));
        await SeedPeriodAsync(db, TestTenantId, student.Id, Now.Date, Now.AddDays(3), isActive: false);

        var service = CreateService(db, new RecordingEmailSender());

        Assert.Equal(0, await service.RunScheduledAsync(Now, CancellationToken.None));
        Assert.Empty(await db.NotificationDeliveries.IgnoreQueryFilters().ToListAsync());
    }

    /// <summary>
    /// NFR-002 — um ciclo em que todo e-mail falha termina e mantém as notificações in-app gravadas.
    /// </summary>
    [Fact]
    public async Task RunScheduledAsync_WhenEveryEmailFails_StillRecordsTheInAppRows()
    {
        using var db = CreateDbContext();
        await SeedTenantAsync(db, TestTenantId, "escola-teste");
        var (student, _) = await SeedStudentAsync(
            db, TestTenantId, "Alice Souza", ("Pai A", "pai-a@test.com"));
        await SeedDocumentAsync(
            db, TestTenantId, student.Id, DocumentStatus.Aprovado, Now.AddDays(10));

        var service = CreateService(db, new RecordingEmailSender("pai-a@test.com"));

        var emitidos = await service.RunScheduledAsync(Now, CancellationToken.None);

        Assert.Equal(1, emitidos);

        var deliveries = await db.NotificationDeliveries.IgnoreQueryFilters().ToListAsync();

        var inApp = Assert.Single(
            deliveries, delivery => delivery.Channel == NotificationDeliveryChannel.InApp);
        Assert.Equal(NotificationDeliveryState.Enviado, inApp.State);

        var email = Assert.Single(
            deliveries, delivery => delivery.Channel == NotificationDeliveryChannel.Email);
        Assert.Equal(NotificationDeliveryState.Falhou, email.State);

        Assert.Single(await db.Notifications.IgnoreQueryFilters().ToListAsync());
    }

    // ── NFR-001: isolamento por tenant ───────────────────────────────

    /// <summary>
    /// Dois tenants com documento a vencer no mesmo repositório: cada linha de entrega sai com o
    /// tenant do documento que a originou e do responsável que a recebeu, nunca com um valor
    /// ambiente, e nenhuma atravessa para o tenant vizinho.
    /// </summary>
    [Fact]
    public async Task RunScheduledAsync_TwoTenants_StampsEachRowWithItsOwnTenant()
    {
        using var db = CreateMultiTenantDbContext();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var tenantAmbiente = Guid.NewGuid();

        await SeedTenantAsync(db, tenantA, "escola-a");
        await SeedTenantAsync(db, tenantB, "escola-b");

        var (studentA, parentsA) = await SeedStudentAsync(
            db, tenantA, "Aluno A", ("Pai A", "pai-a@test.com"));
        var (studentB, parentsB) = await SeedStudentAsync(
            db, tenantB, "Aluno B", ("Pai B", "pai-b@test.com"));

        await SeedDocumentAsync(db, tenantA, studentA.Id, DocumentStatus.Aprovado, Now.AddDays(5));
        await SeedDocumentAsync(db, tenantB, studentB.Id, DocumentStatus.Aprovado, Now.AddDays(5));

        var service = CreateService(db, new RecordingEmailSender());

        // O job roda fora de qualquer requisição: o tenant ambiente é alheio aos dois documentos e
        // não pode vazar para linha alguma (NFR-001).
        Ambient.SetTenant(tenantAmbiente);
        await service.RunScheduledAsync(Now, CancellationToken.None);

        var deliveries = await db.NotificationDeliveries.IgnoreQueryFilters().ToListAsync();
        var notifications = await db.Notifications.IgnoreQueryFilters().ToListAsync();
        var documents = await db.Documents.IgnoreQueryFilters().ToListAsync();
        var users = await db.Users.IgnoreQueryFilters().ToListAsync();

        Assert.NotEmpty(deliveries);
        Assert.NotEmpty(notifications);

        // Toda linha pertence a um dos tenants donos dos documentos, nunca ao tenant ambiente.
        var tenantsComDocumento = new[] { tenantA, tenantB };
        Assert.All(deliveries, delivery =>
            Assert.Contains(delivery.TenantId, tenantsComDocumento));

        // Cada linha carrega o tenant do responsável que a recebeu.
        Assert.All(deliveries, delivery =>
        {
            var destinatario = users.Single(user => user.Id == delivery.RecipientUserId);
            Assert.Equal(destinatario.TenantId, delivery.TenantId);
        });

        // Cada notificação carrega o tenant do documento que a originou.
        Assert.All(notifications, notification =>
        {
            var documento = documents.Single(document => document.Id == notification.ReferenceId);
            Assert.Equal(documento.TenantId, notification.TenantId);
        });

        // O responsável de um tenant jamais recebe linha do outro.
        Assert.DoesNotContain(deliveries, delivery =>
            delivery.TenantId != tenantA && delivery.RecipientUserId == parentsA[0].Id);
        Assert.DoesNotContain(deliveries, delivery =>
            delivery.TenantId != tenantB && delivery.RecipientUserId == parentsB[0].Id);
    }

    // ── FR-003: catálogo de templates ────────────────────────────────

    /// <summary>Todo tipo resolve assunto e corpo não vazios, inclusive com locale desconhecido.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("pt-BR")]
    [InlineData(" pt-br ")]
    [InlineData("en-US")]
    public void Resolve_ForEveryKind_ReturnsNonEmptyTemplate(string? locale)
    {
        foreach (var kind in Enum.GetValues<NotificationTriggerKind>())
        {
            var template = NotificationTemplateCatalog.Resolve(kind, locale);

            Assert.False(string.IsNullOrWhiteSpace(template.Assunto));
            Assert.False(string.IsNullOrWhiteSpace(template.Corpo));
        }
    }

    /// <summary>Locale desconhecido cai para o catálogo pt-BR, sem exceção e sem corpo vazio.</summary>
    [Fact]
    public void Resolve_ForUnknownLocale_FallsBackToPtBr()
    {
        foreach (var kind in Enum.GetValues<NotificationTriggerKind>())
        {
            var ptBr = NotificationTemplateCatalog.Resolve(
                kind, NotificationTemplateCatalog.DefaultLocale);
            var desconhecido = NotificationTemplateCatalog.Resolve(kind, "xx-YY");

            Assert.Equal(ptBr, desconhecido);
        }
    }

    // ── FR-004: provedor SMTP fail-closed ────────────────────────────

    /// <summary>Host em branco falha na construção, com o código que a operação consegue procurar.</summary>
    [Fact]
    public void SmtpEmailSender_WithMissingHost_ThrowsWithMissingConfigurationCode()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:Smtp:Host"] = " ",
                ["Email:Smtp:Porta"] = "587",
                ["Email:Smtp:Remetente"] = "no-reply@escola.test"
            })
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() => new SmtpEmailSender(configuration));

        Assert.Contains(SmtpEmailSender.MissingConfigurationCode, ex.Message, StringComparison.Ordinal);
        Assert.Contains("Email:Smtp:Host", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>Configuração completa constrói sem lançar — o fail-closed só dispara no que falta.</summary>
    [Fact]
    public void SmtpEmailSender_WithCompleteConfiguration_Constructs()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:Smtp:Host"] = "smtp.escola.test",
                ["Email:Smtp:Porta"] = "587",
                ["Email:Smtp:Remetente"] = "no-reply@escola.test",
                ["Email:Smtp:Usuario"] = "usuario",
                ["Email:Smtp:Senha"] = "segredo"
            })
            .Build();

        var sender = new SmtpEmailSender(configuration);

        Assert.NotNull(sender);
    }

    // ── Infraestrutura de teste ──────────────────────────────────────

    private static ProactiveTestDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"proactive-notifications-{Guid.NewGuid():N}")
            .Options;

        return new ProactiveTestDbContext(options, Ambient);
    }

    /// <summary>
    /// Contexto exclusivo do cenário de dois tenants. O plano de consulta do provedor interno fica
    /// preso ao tenant da primeira execução de cada forma de consulta, então compartilhar o tipo com os
    /// demais testes deste arquivo tornaria este cenário dependente da ordem de execução.
    /// </summary>
    private static MultiTenantTestDbContext CreateMultiTenantDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"proactive-notifications-multi-{Guid.NewGuid():N}")
            .Options;

        return new MultiTenantTestDbContext(options, Ambient);
    }

    private static ProactiveNotificationService CreateService(AppDbContext db, IEmailSender sender) =>
        new(db, sender, Ambient, NullLogger<ProactiveNotificationService>.Instance);

    private static async Task SeedTenantAsync(AppDbContext db, Guid tenantId, string slug)
    {
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = $"Escola {slug}",
            Slug = slug,
            Country = "BR",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
    }

    private static async Task<(Student Student, List<User> Parents)> SeedStudentAsync(
        AppDbContext db, Guid tenantId, string nome,
        params (string Nome, string? Email)[] parents)
    {
        var student = new Student
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Nome = nome,
            DataNascimento = new DateTime(2015, 5, 20, 0, 0, 0, DateTimeKind.Utc),
            Turma = "5A",
            AnoLetivo = 2026,
            Status = StudentStatus.Ativo,
            CreatedAt = DateTime.UtcNow
        };

        var users = parents.Select(parent => new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = parent.Nome,
            Email = parent.Email,
            UserName = parent.Email ?? parent.Nome,
            Role = UserRole.Parent,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        }).ToList();

        db.Students.Add(student);
        db.Users.AddRange(users);
        db.StudentParents.AddRange(users.Select(user => new StudentParent
        {
            StudentId = student.Id,
            ParentId = user.Id
        }));

        await db.SaveChangesAsync();
        return (student, users);
    }

    private static async Task<Document> SeedDocumentAsync(
        AppDbContext db, Guid tenantId, Guid studentId, DocumentStatus status, DateTime? dataValidade)
    {
        var tipo = new DocumentType
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Nome = "Comprovante de residência",
            IsRequired = true,
            IsActive = true
        };

        var document = new Document
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            StudentId = studentId,
            DocumentTypeId = tipo.Id,
            NomeArquivo = "comprovante.pdf",
            CaminhoArquivo = "arquivo",
            Status = status,
            DataValidade = dataValidade,
            CreatedAt = DateTime.UtcNow
        };

        db.DocumentTypes.Add(tipo);
        db.Documents.Add(document);
        await db.SaveChangesAsync();
        return document;
    }

    private static async Task<EnrollmentPeriod> SeedPeriodAsync(
        AppDbContext db, Guid tenantId, Guid studentId,
        DateTime dataInicio, DateTime dataFim, bool isActive)
    {
        var period = new EnrollmentPeriod
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Nome = "Rematrícula 2027",
            DataInicio = dataInicio,
            DataFim = dataFim,
            AnoLetivo = 2027,
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow
        };

        db.EnrollmentPeriods.Add(period);
        db.Enrollments.Add(new Enrollment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            StudentId = studentId,
            EnrollmentPeriodId = period.Id,
            Status = EnrollmentStatus.Aprovado,
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
        return period;
    }

    /// <summary>
    /// Tipo de contexto próprio: o modelo do EF é cacheado por tipo, e é ele que guarda o filtro de
    /// tenant. Herdar o <see cref="AppDbContext"/> puro faria este arquivo compartilhar o modelo de
    /// outro teste e filtrar por um tenant alheio.
    /// </summary>
    private sealed class ProactiveTestDbContext : AppDbContext
    {
        internal ProactiveTestDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
            : base(options, tenantContext) { }
    }

    /// <summary>
    /// Tipo próprio só para o cenário de dois tenants: modelo e plano de consulta são por tipo de
    /// contexto, e o cenário precisa que a forma de consulta nasça com os tenants dele.
    /// </summary>
    private sealed class MultiTenantTestDbContext : AppDbContext
    {
        internal MultiTenantTestDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
            : base(options, tenantContext) { }
    }

    /// <summary>Tenant mutável, para o ciclo agendado poder trocar de tenant no meio da execução.</summary>
    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid TenantId { get; private set; }

        public bool IsResolved { get; private set; }

        public void SetTenant(Guid tenantId)
        {
            TenantId = tenantId;
            IsResolved = tenantId != Guid.Empty;
        }
    }

    /// <summary>Dublê sem rede: registra os envios e recusa os endereços pedidos pelo teste.</summary>
    private sealed class RecordingEmailSender : IEmailSender
    {
        private readonly HashSet<string> _failingAddresses;

        public RecordingEmailSender(params string[] failingAddresses) =>
            _failingAddresses = new HashSet<string>(failingAddresses, StringComparer.OrdinalIgnoreCase);

        public List<EmailSent> Sent { get; } = [];

        public Task SendAsync(string toEmail, string toName, string subject, string body)
        {
            if (_failingAddresses.Contains(toEmail))
            {
                throw new InvalidOperationException("smtp_recusado");
            }

            Sent.Add(new EmailSent(toEmail, toName, subject, body));
            return Task.CompletedTask;
        }
    }

    private sealed record EmailSent(string ToEmail, string ToName, string Subject, string Body);
}
