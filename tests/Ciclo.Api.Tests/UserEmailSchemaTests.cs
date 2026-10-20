using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Ciclo.Api.Tests;

/// <summary>
/// C2 — contrato de schema: o e-mail precisa ter índice único GLOBAL, não composto com o tenant.
///
/// A regra de aplicação vive em <c>EmailUniqueness</c>, mas ela depende do banco para ser uma
/// garantia: sob concorrência, duas requisições simultâneas passam pela checagem antes de qualquer
/// insert. Sem o índice, a corrida recria o e-mail duplicado que causa o login ambíguo.
///
/// O índice composto (TenantId, Email) que existia antes é justamente o que permitia o mesmo
/// e-mail em duas escolas — por isso este contrato também exige que ele NÃO seja a única garantia.
/// </summary>
public sealed class UserEmailSchemaTests
{
    [Fact]
    public void Users_HaveAGloballyUniqueIndexOnEmail()
    {
        using var context = CreateContext();
        var userType = context.Model.FindEntityType(typeof(User));

        Assert.NotNull(userType);

        var emailIndex = userType!.GetIndexes()
            .SingleOrDefault(index => index.Properties.Count == 1
                && index.Properties[0].Name == nameof(User.Email));

        Assert.True(emailIndex is not null,
            "Users.Email precisa de um índice único global: o login resolve o usuário só pelo "
            + "e-mail, sem tenant. Sem ele, o mesmo e-mail existe em duas escolas.");

        Assert.True(emailIndex!.IsUnique,
            "O índice de Users.Email precisa ser UNIQUE — sem isso a unicidade deixa de ser "
            + "garantia do banco e passa a depender só da checagem da aplicação (sujeita a corrida).");
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(t => t.IsResolved).Returns(false);

        return new AppDbContext(options, tenantContext.Object);
    }
}
