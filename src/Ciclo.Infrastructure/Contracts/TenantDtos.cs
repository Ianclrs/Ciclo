namespace Ciclo.Infrastructure.Contracts;

/// <summary>
/// Payload público de cadastro de uma nova escola (tenant) com seu usuário administrador.
/// Os campos de documento fiscal, telefone e endereço variam conforme o país (Country).
/// </summary>
public record RegisterSchoolRequest(
    string SchoolName,
    string Country,
    string? DocumentoFiscal,
    string? Telefone,
    string? Endereco,
    string? Cidade,
    string? Estado,
    string? Cep,
    string AdminName,
    string AdminEmail,
    string AdminPassword);

public record RegisterSchoolResponse(Guid TenantId, string Slug, string Country);

/// <summary>
/// Identificação do colégio do usuário logado. Vive no recurso de tenant (e não no UserDto):
/// é dado da escola, não do usuário, e não deve se repetir em cada listagem de usuários.
/// </summary>
public record TenantSummaryDto(Guid Id, string Name, string Slug);
