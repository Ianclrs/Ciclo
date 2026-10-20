using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Email;
using Ciclo.Infrastructure.Services;
using Ciclo.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Ciclo.Api.Tests;

/// <summary>
/// C9 — o envio de e-mail precisa sair do escopo da requisição.
///
/// Antes: <c>_ = SendEmailsAsync(...)</c> a partir do serviço scoped. A task continuava depois da
/// resposta e usava o <c>AppDbContext</c> e o <c>IEmailSender</c> — escopo possivelmente já
/// descartado. A falha caía num <c>catch</c> que logava warning, então o e-mail não saía e ninguém
/// percebia.
///
/// <b>Limite medido deste teste:</b> ele verifica o que é entregue ao dispatcher, não que a entrega
/// aconteça antes do fim da requisição. O defeito só se manifesta quando a consulta ao banco
/// realmente espera I/O — com o provider InMemory ela completa inline, e o teste passa **mesmo** com
/// o fire-and-forget reintroduzido (confirmado por controle negativo). Por isso o guarda real contra
/// a reintrodução é estrutural: ver
/// <see cref="NotificationService_DoesNotTakeAnEmailSenderDirectly"/>.
/// </summary>
public sealed class NotificationEmailHandoffTests
{
    [Fact]
    public async Task CreateAsync_HandsEveryMessageToTheDispatcherBeforeReturning()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateDbContext();
        var (withEmail, alsoWithEmail, withoutEmail) = await SeedParentsAsync(db, tenantId);

        var dispatcher = new RecordingEmailDispatcher();
        var service = new NotificationService(db, dispatcher, NullLogger<NotificationService>.Instance);

        await service.CreateAsync(
            new CreateNotificationRequest(
                "Reunião de pais",
                "A reunião será na sexta.",
                NotificationType.Geral,
                null,
                [withEmail, alsoWithEmail, withoutEmail]),
            tenantId);

        // A partir daqui o escopo da requisição acabou. Com o código anterior, a consulta de
        // usuários ainda estava pendente e estouraria aqui — e o e-mail se perdia.
        db.Dispose();
        await Task.Delay(50);

        Assert.Equal(2, dispatcher.Enqueued.Count);
        Assert.All(dispatcher.Enqueued, message => Assert.Equal("Reunião de pais", message.Subject));

        // Não vaza: o responsável sem e-mail não recebe nada, e ninguém recebe o e-mail de outro.
        Assert.DoesNotContain(dispatcher.Enqueued, m => m.ToEmail == "sem-email@test.com");
        Assert.Equal(
            ExpectedRecipients,
            dispatcher.Enqueued.Select(m => m.ToEmail).OrderBy(e => e, StringComparer.Ordinal));
    }

    private static readonly string[] ExpectedRecipients = ["pai-a@test.com", "pai-b@test.com"];

    /// <summary>
    /// O guarda que de fato impede a reintrodução do defeito — estrutural, e não comportamental.
    ///
    /// Se o serviço voltar a receber um <see cref="IEmailSender"/>, volta a poder enviar dentro da
    /// requisição: nenhuma injeção de e-mail pode estar disponível a ele. O envio tem de ficar
    /// atrás do <see cref="IEmailDispatcher"/>, cuja única implementação de produção consome a fila
    /// em background, com escopo próprio (<c>EmailDispatchService</c>).
    ///
    /// É este o teste que não depende do provider de banco — e por isso o que protege de verdade.
    /// </summary>
    [Fact]
    public void NotificationService_DoesNotTakeAnEmailSenderDirectly()
    {
        var dependencies = typeof(NotificationService)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToList();

        Assert.DoesNotContain(typeof(IEmailSender), dependencies);
        Assert.Contains(typeof(IEmailDispatcher), dependencies);
    }

    private static async Task<(Guid WithEmail, Guid AlsoWithEmail, Guid WithoutEmail)> SeedParentsAsync(
        AppDbContext db, Guid tenantId)
    {
        var withEmail = new User
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Email = "pai-a@test.com",
            UserName = "pai-a@test.com", Name = "Pai A", Role = UserRole.Parent, IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        var alsoWithEmail = new User
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Email = "pai-b@test.com",
            UserName = "pai-b@test.com", Name = "Pai B", Role = UserRole.Parent, IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        var withoutEmail = new User
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Email = null,
            UserName = "sem-email", Name = "Pai C", Role = UserRole.Parent, IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        db.Users.AddRange(withEmail, alsoWithEmail, withoutEmail);
        await db.SaveChangesAsync();
        return (withEmail.Id, alsoWithEmail.Id, withoutEmail.Id);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(t => t.IsResolved).Returns(false);

        return new AppDbContext(options, tenantContext.Object);
    }

    private sealed class RecordingEmailDispatcher : IEmailDispatcher
    {
        public List<EmailMessage> Enqueued { get; } = [];

        public void Enqueue(EmailMessage message) => Enqueued.Add(message);
    }
}

/// <summary>
/// C9 — a política de entrega: retry **limitado** e cancelamento que não é engolido.
///
/// Verificada sem host e sem espera real (o atraso entre tentativas é injetado): o que importa é que
/// o número de tentativas é finito e que o host encerrando não é tratado como falha do envio.
/// </summary>
public sealed class EmailDeliveryTests
{
    private static readonly EmailMessage Sample = new("pai@test.com", "Pai", "Assunto", "Corpo");

    [Fact]
    public async Task TryDeliverAsync_RetriesAndSucceeds_WhenALaterAttemptWorks()
    {
        var sender = new StubEmailSender(failuresBeforeSuccess: 2);
        using var provider = BuildProvider(sender);

        var delivered = await EmailDelivery.TryDeliverAsync(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Sample,
            NullLogger.Instance,
            CancellationToken.None,
            TimeSpan.Zero);

        Assert.True(delivered);
        Assert.Equal(3, sender.Attempts);
    }

    [Fact]
    public async Task TryDeliverAsync_GivesUpAfterTheBoundedNumberOfAttempts()
    {
        var sender = new StubEmailSender(failuresBeforeSuccess: int.MaxValue);
        using var provider = BuildProvider(sender);

        var delivered = await EmailDelivery.TryDeliverAsync(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Sample,
            NullLogger.Instance,
            CancellationToken.None,
            TimeSpan.Zero);

        Assert.False(delivered);
        Assert.Equal(EmailDelivery.MaxAttempts, sender.Attempts);
    }

    /// <summary>
    /// Encerramento do host não é falha de entrega: precisa propagar, e não virar nova tentativa
    /// nem ser registrado como erro permanente.
    /// </summary>
    [Fact]
    public async Task TryDeliverAsync_PropagatesCancellation_WithoutRetrying()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var sender = new StubEmailSender(failuresBeforeSuccess: 0, failure: new OperationCanceledException());
        using var provider = BuildProvider(sender);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => EmailDelivery.TryDeliverAsync(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Sample,
            NullLogger.Instance,
            cancellation.Token,
            TimeSpan.Zero));

        Assert.Equal(1, sender.Attempts);
    }

    private static ServiceProvider BuildProvider(IEmailSender sender)
    {
        var services = new ServiceCollection();
        services.AddSingleton(sender);
        return services.BuildServiceProvider();
    }

    private sealed class StubEmailSender : IEmailSender
    {
        private readonly int _failuresBeforeSuccess;
        private readonly Exception? _failure;

        public StubEmailSender(int failuresBeforeSuccess, Exception? failure = null)
        {
            _failuresBeforeSuccess = failuresBeforeSuccess;
            _failure = failure;
        }

        public int Attempts { get; private set; }

        public Task SendAsync(string toEmail, string toName, string subject, string body)
        {
            Attempts++;

            if (_failure is not null)
            {
                throw _failure;
            }

            if (Attempts <= _failuresBeforeSuccess)
            {
                throw new InvalidOperationException("servidor SMTP indisponível");
            }

            return Task.CompletedTask;
        }
    }
}

/// <summary>
/// C9 — fila cheia precisa ser visível.
///
/// A fila é limitada para não crescer a memória sem teto, mas descartar em silêncio seria trocar um
/// problema por outro: a mensagem perdida tem de aparecer no log como erro.
/// </summary>
public sealed class ChannelEmailDispatcherTests
{
    [Fact]
    public void Enqueue_WhenQueueIsFull_DropsWithAnErrorInsteadOfThrowing()
    {
        var logger = new CapturingLogger<ChannelEmailDispatcher>();
        var dispatcher = new ChannelEmailDispatcher(logger);

        for (var i = 0; i < ChannelEmailDispatcher.Capacity; i++)
        {
            dispatcher.Enqueue(new EmailMessage($"pai-{i}@test.com", "Pai", "Assunto", "Corpo"));
        }

        // Não pode lançar: quem chama já persistiu a notificação in-app.
        dispatcher.Enqueue(new EmailMessage("excedente@test.com", "Pai", "Assunto", "Corpo"));

        Assert.Contains(logger.Messages,
            message => message.Contains("excedente@test.com", StringComparison.Ordinal));
    }

    [Fact]
    public void Enqueue_WhenThereIsRoom_DoesNotLogAnything()
    {
        var logger = new CapturingLogger<ChannelEmailDispatcher>();
        var dispatcher = new ChannelEmailDispatcher(logger);

        dispatcher.Enqueue(new EmailMessage("pai@test.com", "Pai", "Assunto", "Corpo"));

        Assert.Empty(logger.Messages);
    }
}
