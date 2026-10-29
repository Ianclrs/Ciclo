using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Tenancy;

namespace Ciclo.Infrastructure.Services;

public interface ITenantService
{
    Task<RegisterSchoolResponse> RegisterSchoolAsync(RegisterSchoolRequest request);

    /// <summary>Colégio do usuário logado (identificação na interface).</summary>
    Task<TenantSummaryDto> GetCurrentAsync(Guid tenantId);
}

/// <summary>
/// Cadastro público de uma nova escola (tenant) com seu usuário administrador.
/// O país escolhido define quais dados complementares (documento fiscal, telefone,
/// endereço, CEP) são esperados, mas todos são persistidos de forma genérica.
/// </summary>
public class TenantService : ITenantService
{
    // Países suportados no cadastro (ISO 3166-1 alpha-2). Mantenha em sincronia
    // com a configuração do frontend (config/countries.ts).
    private static readonly HashSet<string> SupportedCountries = new(StringComparer.Ordinal)
    {
        "BR", "US", "PT"
    };

    private readonly AppDbContext _dbContext;
    private readonly UserManager<User> _userManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<TenantService> _logger;

    public TenantService(
        AppDbContext dbContext,
        UserManager<User> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        ITenantContext tenantContext,
        ILogger<TenantService> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _roleManager = roleManager;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<RegisterSchoolResponse> RegisterSchoolAsync(RegisterSchoolRequest request)
    {
        var country = NormalizeCountry(request.Country);
        if (!SupportedCountries.Contains(country))
            throw new AuthException("unsupported_country", 400);

        var schoolName = request.SchoolName?.Trim();
        if (string.IsNullOrWhiteSpace(schoolName))
            throw new AuthException("school_name_required", 400);

        // O e-mail é a chave de login: única globalmente — mesma regra do cadastro de responsável
        // (ver EmailUniqueness, que é o dono da regra).
        await EmailUniqueness.EnsureAvailableAsync(_dbContext, request.AdminEmail);

        var slug = await GenerateUniqueSlugAsync(schoolName);

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = schoolName,
            Slug = slug,
            Country = country,
            DocumentoFiscal = NullIfEmpty(request.DocumentoFiscal),
            Telefone = NullIfEmpty(request.Telefone),
            Endereco = NullIfEmpty(request.Endereco),
            Cidade = NullIfEmpty(request.Cidade),
            Estado = NullIfEmpty(request.Estado),
            Cep = NullIfEmpty(request.Cep),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Tenants.Add(tenant);
        await _dbContext.SaveChangesAsync();

        // Resolve o tenant do usuário para as consultas internas do Identity
        // (ex.: FindByNameAsync) não falharem por causa do global query filter.
        _tenantContext.SetTenant(tenant.Id);

        await EnsureRoleAsync(UserRole.Admin.ToString());

        var admin = new User
        {
            TenantId = tenant.Id,
            Email = request.AdminEmail,
            UserName = request.AdminEmail,
            Name = request.AdminName?.Trim() ?? string.Empty,
            Role = UserRole.Admin,
            EmailConfirmed = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(admin, request.AdminPassword);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new AuthException(errors, 400);
        }

        await _userManager.AddToRoleAsync(admin, UserRole.Admin.ToString());

#pragma warning disable CA1848, CA1873
        _logger.LogInformation("School registered: {School} ({Slug}) country {Country}", tenant.Name, tenant.Slug, tenant.Country);
#pragma warning restore CA1848, CA1873

        return new RegisterSchoolResponse(tenant.Id, tenant.Slug, tenant.Country);
    }

    public async Task<TenantSummaryDto> GetCurrentAsync(Guid tenantId)
    {
        var tenant = await _dbContext.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new TenantSummaryDto(t.Id, t.Name, t.Slug))
            .FirstOrDefaultAsync();

        return tenant ?? throw new AuthException("tenant_not_found", 404);
    }

    private static string NormalizeCountry(string? country)
    {
        return (country ?? string.Empty).Trim().ToUpperInvariant();
    }

    private async Task<string> GenerateUniqueSlugAsync(string name)
    {
        var baseSlug = Slugify(name);
        if (!await _dbContext.Tenants.AnyAsync(t => t.Slug == baseSlug))
            return baseSlug;

        // Colisão rara: adiciona um sufixo curto para garantir unicidade.
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var maxPrefix = 100 - 1 - suffix.Length;
        var prefix = baseSlug.Length > maxPrefix ? baseSlug[..maxPrefix] : baseSlug;
        return $"{prefix}-{suffix}";
    }

    private static string Slugify(string name)
    {
        // Remove acentos e gera um slug minúsculo: apenas letras, dígitos e hífens.
        var normalized = name.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        foreach (var c in normalized)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                == System.Globalization.UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(c))
                builder.Append(char.ToLowerInvariant(c));
            else if (c is ' ' or '-' or '_')
                builder.Append('-');
        }

        var slug = Regex.Replace(builder.ToString(), "-+", "-").Trim('-');
        if (slug.Length > 100)
            slug = slug[..100].Trim('-');

        return string.IsNullOrEmpty(slug) ? "escola" : slug;
    }

    private async Task EnsureRoleAsync(string roleName)
    {
        if (!await _roleManager.RoleExistsAsync(roleName))
            await _roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
