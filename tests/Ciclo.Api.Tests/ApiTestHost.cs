using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Ciclo.Api.Tests;

/// <summary>
/// Host único da suíte de integração, compartilhado por <see cref="ICollectionFixture{T}"/>.
///
/// Por que um só: <c>WebApplicationFactory&lt;Program&gt;</c> descobre o host interceptando o
/// entry point por um hook de processo. Duas factories construindo host ao mesmo tempo disputam
/// esse estado global e a suíte falha com "The entry point exited without ever building an
/// IHost" — em uma classe que já existia, o que torna o diagnóstico enganoso (L7 em
/// <c>management/lessons.md</c>). A coleção também serializa as classes que usam o host.
///
/// Os segredos obrigatórios são injetados aqui de propósito: desde C1 <c>Program</c> é
/// fail-closed, e o teste não pode depender de user-secrets da máquina de quem o executa.
/// </summary>
public sealed class ApiTestHost : WebApplicationFactory<Program>
{
    public const string CollectionName = "ApiTestHost";

    public const string JwtSecret = "test-secret-that-is-at-least-32-characters-long!!";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Ambiente "Testing", explícito e não herdado. O padrão da factory é Development, e desde a
        // decisão A1 (2026-10-16) o caminho de Development aplica migrations e roda os seeders —
        // ou seja, a suíte passaria a MEXER no banco de desenvolvimento a cada execução, e quebraria
        // de forma confusa quando o Postgres não estivesse no ar. O Program trata "Testing" como
        // "nenhuma gestão de schema".
        builder.UseEnvironment("Testing");

        // UseSetting (e não ConfigureAppConfiguration): no minimal hosting o Program lê
        // builder.Configuration, e só as chaves do host chegam até lá.
        builder.UseSetting("Jwt:Secret", JwtSecret);
        builder.UseSetting("Jwt:Issuer", "Ciclo");
        builder.UseSetting("Jwt:Audience", "Ciclo");
        builder.UseSetting("FileStorage:EncryptionKey", Convert.ToBase64String(new byte[32]));
    }
}

[CollectionDefinition(ApiTestHost.CollectionName)]
public sealed class ApiTestHostGroup : ICollectionFixture<ApiTestHost>
{
}
