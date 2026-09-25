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
