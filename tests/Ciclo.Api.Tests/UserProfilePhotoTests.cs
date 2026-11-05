using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Ciclo.Core.Auditing;
using Ciclo.Core.Entities;
using Ciclo.Core.Exceptions;
using Ciclo.Infrastructure.Auth;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Services;
using Ciclo.Infrastructure.Tenancy;

namespace Ciclo.Api.Tests;

/// <summary>
/// Foto de perfil do usuário (Admin, Staff e responsável). A regra de formato e tamanho é a MESMA
/// da foto do aluno, cujo dono é <c>ImageDataUrl</c>; estes testes fixam os quatro desfechos que o
/// endpoint <c>PUT api/auth/me/photo</c> pode devolver.
/// </summary>
public sealed class UserProfilePhotoTests : IDisposable
{
    /// <summary>PNG 1x1 válido: prefixo permitido + assinatura coerente.</summary>
    private const string ValidPng =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==";

    private sealed class FixedTenantContext : ITenantContext
    {
        public Guid TenantId { get; private set; }
        public bool IsResolved => TenantId != Guid.Empty;
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private readonly AppDbContext _db;
    private readonly AuthService _service;
    private readonly User _user;

    public UserProfilePhotoTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"user-photo-{Guid.NewGuid()}")
            .Options;

        var tenantContext = new FixedTenantContext();
        _db = new AppDbContext(options, tenantContext);

        var userStore = new Mock<IUserStore<User>>();
        var userManager = new Mock<UserManager<User>>(
            userStore.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        _service = new AuthService(
            userManager.Object,
            new Mock<IJwtTokenGenerator>().Object,
            _db,
            tenantContext,
            NullLogger<AuthService>.Instance,
            new Mock<IAuditTrail>().Object,
            new Mock<ITenantService>().Object);

        _user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Email = "admin@aurora.com",
            UserName = "admin@aurora.com",
            Name = "Admin Aurora",
            Role = UserRole.Admin,
        };

        _db.Users.Add(_user);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task UpdateMyPhotoAsync_ValidImage_SavesAndReturnsIt()
    {
        var dto = await _service.UpdateMyPhotoAsync(_user.Id, ValidPng);

        Assert.Equal(ValidPng, dto.Foto);
        Assert.Equal(ValidPng, (await _db.Users.FirstAsync(u => u.Id == _user.Id)).Foto);
    }

    [Fact]
    public async Task UpdateMyPhotoAsync_Null_RemovesPhoto()
    {
        await _service.UpdateMyPhotoAsync(_user.Id, ValidPng);

        var dto = await _service.UpdateMyPhotoAsync(_user.Id, null);

        Assert.Null(dto.Foto);
        Assert.Null((await _db.Users.FirstAsync(u => u.Id == _user.Id)).Foto);
    }

    [Fact]
    public async Task UpdateMyPhotoAsync_Svg_IsRejected()
    {
        // SVG executa script quando aberto no navegador: fica fora da lista fechada de formatos.
        var error = await Assert.ThrowsAsync<AuthException>(() =>
            _service.UpdateMyPhotoAsync(_user.Id, "data:image/svg+xml;base64,PHN2Zy8+"));

        Assert.Equal(400, error.StatusCode);
        Assert.Equal("foto_invalid_format", error.Message);
    }

    [Fact]
    public async Task UpdateMyPhotoAsync_AboveLimit_IsRejected()
    {
        var tooLarge = "data:image/png;base64," + new string('A', 200_000);

        var error = await Assert.ThrowsAsync<AuthException>(() =>
            _service.UpdateMyPhotoAsync(_user.Id, tooLarge));

        Assert.Equal(400, error.StatusCode);
        Assert.Equal("foto_too_large", error.Message);
    }

    [Fact]
    public async Task UpdateMyPhotoAsync_UnknownUser_FailsWith404()
    {
        var error = await Assert.ThrowsAsync<AuthException>(() =>
            _service.UpdateMyPhotoAsync(Guid.NewGuid(), ValidPng));

        Assert.Equal(404, error.StatusCode);
        Assert.Equal("user_not_found", error.Message);
    }
}
