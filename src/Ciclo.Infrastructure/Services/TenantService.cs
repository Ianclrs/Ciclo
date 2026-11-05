using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
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

    /// <summary>Configuração efetiva da escola; leitura cacheada por tenant (Spec 200 / FR-002).</summary>
    Task<TenantConfigurationDto> GetConfigurationAsync(Guid tenantId);

    /// <summary>Atualização parcial da configuração; campo nulo preserva o valor (Spec 200 / FR-003).</summary>
    Task<TenantConfigurationDto> UpdateConfigurationAsync(Guid tenantId, UpdateTenantConfigurationRequest request);

    /// <summary>Recusa a criação de usuário quando a cota do tenant está esgotada (Spec 200 / FR-004).</summary>
    Task EnsureUserQuotaAsync(Guid tenantId);
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

    // Locales aceitos na configuração da escola (Spec 200). O locale efetivo da interface
    // continua sendo resolvido pela spec 190 — aqui ele é apenas persistido.
    private static readonly HashSet<string> SupportedLocales = new(StringComparer.Ordinal)
    {
        "pt-BR", "en-US", "es-ES"
    };

    // Spec 200 — ADR-200-3: uma chave por tenant, expiração absoluta de 5 minutos e
    // invalidação explícita na escrita. Não há cache distribuído nem abstração própria.
    private const string ConfigurationCachePrefix = "tenant-config:";
    private static readonly TimeSpan ConfigurationCacheDuration = TimeSpan.FromMinutes(5);

    private readonly AppDbContext _dbContext;
    private readonly UserManager<User> _userManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
    private readonly ITenantContext _tenantContext;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TenantService> _logger;

    public TenantService(
        AppDbContext dbContext,
        UserManager<User> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        ITenantContext tenantContext,
        IMemoryCache cache,
        ILogger<TenantService> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _roleManager = roleManager;
        _tenantContext = tenantContext;
        _cache = cache;
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
            CreatedAt = DateTime.UtcNow,

            // O tenant nasce com o conjunto padrão do próprio plano (ADR-200-2). Sem isso a tela
            // de configuração abriria com todas as funcionalidades desmarcadas para uma escola
            // recém-cadastrada, contradizendo o plano que ela já declara.
            Plan = TenantPlan.Basic,
            FeatureFlags = TenantFeature.DefaultCsv(TenantPlan.Basic)
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

    public async Task<TenantConfigurationDto> GetConfigurationAsync(Guid tenantId)
    {
        var cacheKey = ConfigurationCachePrefix + tenantId;
        if (_cache.TryGetValue(cacheKey, out TenantConfigurationDto? cached) && cached is not null)
            return cached;

        var tenant = await _dbContext.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId && t.IsActive);

        if (tenant is null)
            throw new AuthException("tenant_not_found", 404);

        var configuration = ToConfigurationDto(tenant);
        _cache.Set(cacheKey, configuration, ConfigurationCacheDuration);
        return configuration;
    }

    public async Task<TenantConfigurationDto> UpdateConfigurationAsync(
        Guid tenantId, UpdateTenantConfigurationRequest request)
    {
        var tenant = await _dbContext.Tenants
            .FirstOrDefaultAsync(t => t.Id == tenantId && t.IsActive);

        if (tenant is null)
            throw new AuthException("tenant_not_found", 404);

        // Toda validação acontece antes de qualquer atribuição: requisição inválida não altera
        // banco nem cache.
        var displayName = request.DisplayName is null ? null : NullIfEmpty(request.DisplayName);
        var logoUrl = request.LogoUrl is null ? null : NullIfEmpty(request.LogoUrl);
        var primaryColor = ValidateColor(request.PrimaryColor);
        var secondaryColor = ValidateColor(request.SecondaryColor);
        var timeZone = request.TimeZone is null ? null : ValidateTimeZone(request.TimeZone);
        var schoolYear = request.DefaultSchoolYear is null
            ? (int?)null
            : ValidateSchoolYear(request.DefaultSchoolYear.Value);
        var locale = request.Locale is null ? null : ValidateLocale(request.Locale);
        var storageQuota = request.StorageQuotaBytes is null
            ? (long?)null
            : ValidateStorageQuota(request.StorageQuotaBytes.Value);
        var userQuota = request.UserQuota is null
            ? (int?)null
            : ValidateUserQuota(request.UserQuota.Value);
        var plan = request.Plan is null ? (TenantPlan?)null : ParsePlan(request.Plan);
        var featureFlags = ValidateFeatureFlags(request.FeatureFlags);

        var hasChanges =
            request.DisplayName is not null
            || request.LogoUrl is not null
            || request.PrimaryColor is not null
            || request.SecondaryColor is not null
            || request.TimeZone is not null
            || request.DefaultSchoolYear is not null
            || request.Locale is not null
            || request.StorageQuotaBytes is not null
            || request.UserQuota is not null
            || request.Plan is not null
            || request.FeatureFlags is not null;

        // Requisição sem nenhum campo devolve o estado atual sem escrever no banco.
        if (!hasChanges)
            return ToConfigurationDto(tenant);

        if (request.DisplayName is not null)
            tenant.DisplayName = displayName;

        if (request.LogoUrl is not null)
            tenant.LogoUrl = logoUrl;

        if (request.PrimaryColor is not null)
            tenant.PrimaryColor = primaryColor;

        if (request.SecondaryColor is not null)
            tenant.SecondaryColor = secondaryColor;

        if (timeZone is not null)
            tenant.TimeZone = timeZone;

        if (schoolYear is not null)
            tenant.DefaultSchoolYear = schoolYear.Value;

        if (locale is not null)
            tenant.Locale = locale;

        if (storageQuota is not null)
            tenant.StorageQuotaBytes = storageQuota.Value;

        if (userQuota is not null)
            tenant.UserQuota = userQuota.Value;

        if (plan is not null)
            tenant.Plan = plan.Value;

        // Flags informadas valem exatamente o conjunto recebido; omitidas com plano informado
        // voltam ao conjunto padrão do plano (ADR-200-2). Sem plano informado o conjunto atual é
        // preservado — editar a cor não pode descartar flags customizadas.
        if (featureFlags is not null)
            tenant.FeatureFlags = TenantFeature.ToCsv(featureFlags);
        else if (plan is not null)
            tenant.FeatureFlags = TenantFeature.ToCsv(TenantFeature.DefaultFor(plan.Value));

        await _dbContext.SaveChangesAsync();

        // Invalidação explícita depois da persistência: a próxima leitura vê o estado novo.
        _cache.Remove(ConfigurationCachePrefix + tenantId);

        return ToConfigurationDto(tenant);
    }

    public async Task EnsureUserQuotaAsync(Guid tenantId)
    {
        // Cota é lida do banco, sem cache: uma redução de cota precisa valer na criação seguinte.
        var userQuota = await _dbContext.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId && t.IsActive)
            .Select(t => (int?)t.UserQuota)
            .FirstOrDefaultAsync();

        if (userQuota is null)
            throw new AuthException("tenant_not_found", 404);

        var userCount = await _dbContext.Users.CountAsync(u => u.TenantId == tenantId);
        if (userCount >= userQuota.Value)
            throw new AuthException("user_quota_exceeded", 409);
    }

    private static TenantConfigurationDto ToConfigurationDto(Tenant tenant) => new(
        tenant.Id,
        string.IsNullOrWhiteSpace(tenant.DisplayName) ? tenant.Name : tenant.DisplayName,
        tenant.LogoUrl,
        tenant.PrimaryColor,
        tenant.SecondaryColor,
        tenant.TimeZone,
        tenant.DefaultSchoolYear,
        tenant.Locale,
        tenant.StorageQuotaBytes,
        tenant.UserQuota,
        tenant.Plan.ToString(),
        SafeFeatureFlags(tenant.FeatureFlags));

    /// <summary>
    /// Leitura tolerante: dado legado com flag desconhecida devolve a interseção válida em vez de
    /// quebrar a leitura. A escrita continua recusando nomes desconhecidos.
    /// </summary>
    private static IReadOnlyList<string> SafeFeatureFlags(string? csv)
    {
        try
        {
            return TenantFeature.Normalize(csv);
        }
        catch (ArgumentException)
        {
            var known = (csv ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(TenantFeature.All.Contains)
                .ToArray();

            return TenantFeature.Normalize(string.Join(',', known));
        }
    }

    private static IReadOnlyList<string>? ValidateFeatureFlags(IReadOnlyList<string>? flags)
    {
        if (flags is null)
            return null;

        try
        {
            return TenantFeature.Normalize(string.Join(',', flags));
        }
        catch (ArgumentException)
        {
            throw new AuthException("invalid_feature_flag", 400);
        }
    }

    private static string? ValidateColor(string? value)
    {
        var color = NullIfEmpty(value);
        if (color is null)
            return null;

        if (!Regex.IsMatch(color, "^#[0-9A-Fa-f]{6}$"))
            throw new AuthException("invalid_color", 400);

        return color.ToUpperInvariant();
    }

    private static string ValidateTimeZone(string value)
    {
        var timeZone = value.Trim();
        if (timeZone.Length == 0)
            throw new AuthException("invalid_time_zone", 400);

        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(timeZone);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new AuthException("invalid_time_zone", 400);
        }

        return timeZone;
    }

    private static int ValidateSchoolYear(int value)
    {
        if (value is < 2000 or > 2100)
            throw new AuthException("invalid_school_year", 400);

        return value;
    }

    private static string ValidateLocale(string value)
    {
        var locale = value.Trim();
        if (!SupportedLocales.Contains(locale))
            throw new AuthException("invalid_locale", 400);

        return locale;
    }

    private static long ValidateStorageQuota(long value)
    {
        if (value <= 0)
            throw new AuthException("invalid_storage_quota", 400);

        return value;
    }

    private static int ValidateUserQuota(int value)
    {
        if (value is < 1 or > 10000)
            throw new AuthException("invalid_user_quota", 400);

        return value;
    }

    private static TenantPlan ParsePlan(string value)
    {
        var name = value.Trim();
        foreach (var plan in Enum.GetValues<TenantPlan>())
        {
            if (string.Equals(plan.ToString(), name, StringComparison.OrdinalIgnoreCase))
                return plan;
        }

        throw new AuthException("invalid_plan", 400);
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
