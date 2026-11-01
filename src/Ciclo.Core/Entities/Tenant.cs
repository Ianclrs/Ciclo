using System.ComponentModel.DataAnnotations;

namespace Ciclo.Core.Entities;

public class Tenant
{
    public Guid Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    [RegularExpression(@"^[a-z0-9]+(?:-[a-z0-9]+)*$",
        ErrorMessage = "Slug must contain only lowercase alphanumeric characters and hyphens.")]
    public string Slug { get; set; } = string.Empty;

    /// <summary>Código do país (ISO 3166-1 alpha-2): "BR", "US", "PT".</summary>
    [Required]
    [MaxLength(2)]
    public string Country { get; set; } = string.Empty;

    /// <summary>Documento fiscal da escola (CNPJ/EIN/NIPC), varia por país.</summary>
    [MaxLength(30)]
    public string? DocumentoFiscal { get; set; }

    [MaxLength(30)]
    public string? Telefone { get; set; }

    [MaxLength(200)]
    public string? Endereco { get; set; }

    [MaxLength(100)]
    public string? Cidade { get; set; }

    [MaxLength(100)]
    public string? Estado { get; set; }

    /// <summary>CEP / ZIP Code / Código Postal — rótulo e formato variam por país.</summary>
    [MaxLength(20)]
    public string? Cep { get; set; }

    /// <summary>Cota de armazenamento de documentos em bytes (FR-006).</summary>
    public long StorageQuotaBytes { get; set; } = 1_073_741_824;

    /// <summary>Nome exibido da escola; quando vazio a API devolve <see cref="Name"/>.</summary>
    [MaxLength(200)]
    public string? DisplayName { get; set; }

    [MaxLength(500)]
    public string? LogoUrl { get; set; }

    /// <summary>Cor primária no formato hexadecimal de seis dígitos, ex.: "#1F6FEB".</summary>
    [MaxLength(7)]
    public string? PrimaryColor { get; set; }

    /// <summary>Cor secundária no formato hexadecimal de seis dígitos, ex.: "#0B3D91".</summary>
    [MaxLength(7)]
    public string? SecondaryColor { get; set; }

    [MaxLength(100)]
    public string TimeZone { get; set; } = "UTC";

    public int DefaultSchoolYear { get; set; } = 2025;

    [MaxLength(5)]
    public string Locale { get; set; } = "pt-BR";

    public TenantPlan Plan { get; set; } = TenantPlan.Basic;

    /// <summary>Flags de funcionalidade efetivas do tenant, separadas por vírgula.</summary>
    [MaxLength(500)]
    public string FeatureFlags { get; set; } = string.Empty;

    public int UserQuota { get; set; } = 50;

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
