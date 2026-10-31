using Ciclo.Infrastructure.Auditing;

namespace Ciclo.Api.Tests;

/// <summary>
/// Spec 110 — contexto do ator preenchido na borda (FR-004).
///
/// Os limites são os mesmos das colunas persistidas: truncar aqui evita que um valor maior que a
/// coluna derrube a operação de negócio no momento do commit.
/// </summary>
public sealed class AuditActorTests
{
    [Fact]
    public void Capture_TruncatesUserAgentTo512()
    {
        var actor = new AuditActor();

        actor.Capture(
            Guid.NewGuid(),
            new string('r', 100),
            new string('1', 100),
            new string('a', 5000));

        Assert.Equal(AuditActor.MaxRoleLength, actor.Role!.Length);
        Assert.Equal(AuditActor.MaxIpAddressLength, actor.IpAddress!.Length);
        Assert.Equal(AuditActor.MaxUserAgentLength, actor.UserAgent!.Length);
        Assert.Equal(new string('a', AuditActor.MaxUserAgentLength), actor.UserAgent);
    }

    [Fact]
    public void Capture_NormalizesEmptyValuesToNull()
    {
        var actor = new AuditActor();

        actor.Capture(null, string.Empty, null, string.Empty);

        Assert.Null(actor.UserId);
        Assert.Null(actor.Role);
        Assert.Null(actor.IpAddress);
        Assert.Null(actor.UserAgent);
    }
}
