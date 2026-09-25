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

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
