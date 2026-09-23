namespace Ciclo.Infrastructure.Contracts;

public record CreateDocumentTypeRequest(string Nome, string? Descricao, bool IsRequired, int ValidadeMeses);

public record DocumentTypeDto(Guid Id, string Nome, string? Descricao, bool IsRequired, int ValidadeMeses, bool IsActive);

public record DocumentDto(
    Guid Id,
    Guid StudentId,
    string StudentName,
    Guid DocumentTypeId,
    string DocumentTypeName,
    string NomeArquivo,
    string Status,
    DateTime? DataValidade,
    string? MotivoRejeicao,
    DateTime CreatedAt,
    // Foto do aluno (data URL). Preenchido apenas nos endpoints que exibem o avatar.
    string? StudentFoto = null);

public record VerifyDocumentRequest(bool Approved, string? MotivoRejeicao);
