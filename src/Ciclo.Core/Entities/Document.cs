using Ciclo.Core.Interfaces;

namespace Ciclo.Core.Entities;

public class Document : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid StudentId { get; set; }
    public Guid DocumentTypeId { get; set; }
    public string NomeArquivo { get; set; } = string.Empty;
    public string CaminhoArquivo { get; set; } = string.Empty;

    /// <summary>Tamanho do conteúdo em bytes, medido na gravação (FR-004).</summary>
    public long TamanhoBytes { get; set; }

    /// <summary>SHA-256 do conteúdo em 64 hexadecimais maiúsculos (FR-004).</summary>
    public string HashSha256 { get; set; } = string.Empty;

    /// <summary>MIME detectado pela assinatura real do conteúdo (FR-004).</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>Autor do upload (FR-004).</summary>
    public Guid EnviadoPorUsuarioId { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.Pendente;
    public DateTime? DataValidade { get; set; }
    public string? MotivoRejeicao { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? VerifiedAt { get; set; }

    public Student Student { get; set; } = null!;
    public DocumentType DocumentType { get; set; } = null!;
    public Tenant Tenant { get; set; } = null!;
}

public enum DocumentStatus
{
    Pendente = 0,
    Aprovado = 1,
    Rejeitado = 2
}
