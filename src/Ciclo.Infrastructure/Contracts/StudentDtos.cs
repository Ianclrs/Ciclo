namespace Ciclo.Infrastructure.Contracts;

public record StudentDto(
    Guid Id,
    string Nome,
    DateTime DataNascimento,
    string? Cpf,
    string Turma,
    int AnoLetivo,
    string Status,
    string? Observacoes,
    string? Foto,
    DateTime CreatedAt,
    List<ParentLinkDto> Parents);

public record CreateStudentRequest(
    string Nome,
    DateTime DataNascimento,
    string? Cpf,
    string Turma,
    int AnoLetivo,
    string? Observacoes,
    string? Foto);

public record UpdateStudentRequest(
    string Nome,
    DateTime DataNascimento,
    string? Cpf,
    string Turma,
    int AnoLetivo,
    string? Observacoes,
    string? Foto);

public record LinkParentRequest(Guid ParentId);

public record ParentLinkDto(Guid ParentId, string ParentName, string ParentEmail);

public record PagedResponse<T>(List<T> Items, int Total, int Page, int PageSize);
