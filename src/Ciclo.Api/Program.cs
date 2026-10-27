using System.Globalization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Ciclo.Core.Entities;
using Ciclo.Core.Interfaces;
using Ciclo.Infrastructure;
using Ciclo.Infrastructure.Auth;
using Ciclo.Infrastructure.Auditing;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Services;
using Ciclo.Infrastructure.Storage;
using Ciclo.Infrastructure.Tenancy;
using Ciclo.Infrastructure.Email;
using Ciclo.Api.Middleware;
using Ciclo.Api.RateLimiting;
using Ciclo.Infrastructure.Data.Seeders;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Serilog
    builder.Host.UseSerilog((context, services, configuration) =>
        configuration.ReadFrom.Configuration(context.Configuration)
            .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture));

    // Services
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
    builder.Services.AddInfrastructure(builder.Configuration);

    // Auditoria (Spec 110) — configuração fail-closed e porta de escrita transversal.
    builder.Services.AddSingleton(AuditSettings.Read(builder.Configuration));
    builder.Services.AddScoped<AuditActor>();
    builder.Services.AddScoped<IAuditActor>(sp => sp.GetRequiredService<AuditActor>());
    builder.Services.AddScoped<IAuditTrail, AuditTrail>();
    builder.Services.AddScoped<IAuditQueryService, AuditQueryService>();
    builder.Services.AddScoped<AuditRetentionPurger>();

    if (!builder.Environment.IsEnvironment("Testing"))
    {
        builder.Services.AddHostedService<AuditRetentionService>();
    }

    // Multi-tenancy (Spec 20)
    builder.Services.AddScoped<TenantContext>();
    builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

    // Identity (Spec 30)
    builder.Services.AddIdentityCore<User>(options =>
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireUppercase = true;
        options.Password.RequireDigit = true;
        options.Password.RequireNonAlphanumeric = true;
        options.User.RequireUniqueEmail = false; // We enforce uniqueness per tenant
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(30);
        options.Lockout.AllowedForNewUsers = true;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

    // JWT Authentication (Spec 30)
    // Fail-closed: JwtSettings.Read lança quando o segredo está ausente, vazio ou com menos
    // de 32 bytes. Não existe valor padrão — um segredo embutido no repositório permitiria
    // forjar tokens em qualquer ambiente que não o sobrescrevesse.
    var jwtSettings = JwtSettings.Read(builder.Configuration);

    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = jwtSettings.CreateValidationParameters();

        // M3 - sem isto, a recusa do framework de autenticacao sai SEM corpo (401/403 vazios) e o
        // cliente nao tem codigo nenhum para decidir; o formato canonico passa a valer tambem aqui.
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                if (context.Response.HasStarted)
                {
                    return;
                }

                await ProblemResponse.WriteAsync(
                    context.HttpContext, StatusCodes.Status401Unauthorized, "unauthorized", "unauthorized");
            },
            OnForbidden = async context =>
            {
                if (context.Response.HasStarted)
                {
                    return;
                }

                await ProblemResponse.WriteAsync(
                    context.HttpContext, StatusCodes.Status403Forbidden, "forbidden", "forbidden");
            }
        };
    });

    // Google OAuth: fluxo removido por decisão explícita (ver management/cross-session/
    // plano-correcoes.md, item C8). O fluxo existia quebrado e no pior lugar possível: ligava
    // a conta Google a um usuário existente pelo e-mail, sem validar o e-mail e sem filtrar
    // tenant — ou seja, tomada de conta. O botão continua no frontend; não há fluxo por trás
    // dele. Reintroduzir exige implementar a validação e remover o contrato de
    // ApiRouteConventionTests que proíbe as rotas google.

    // Authorization (Spec 30) — deny-by-default
    // Sem FallbackPolicy, um endpoint sem [Authorize] fica público por esquecimento, e não há
    // como distinguir "público por decisão" de "esquecido". Com a política, esquecer o atributo
    // nega em vez de expor, e todo endpoint público passa a exigir um [AllowAnonymous] explícito
    // — verificado por ApiAuthorizationTests.
    builder.Services.AddAuthorization(options =>
    {
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
    });

    // Auth services (Spec 30)
    builder.Services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
    builder.Services.AddScoped<IAuthService, AuthService>();

    // Tenant services (school registration)
    builder.Services.AddScoped<ITenantService, TenantService>();

    // Student services (Spec 40)
    builder.Services.AddScoped<IStudentService, StudentService>();

    // Enrollment services (Spec 50)
    builder.Services.AddScoped<IEnrollmentService, EnrollmentService>();


    // Document services (Spec 70)
    builder.Services.Configure<FileStorageOptions>(builder.Configuration.GetSection(FileStorageOptions.SectionName));
    // Fail-closed: a chave de criptografia é validada agora, não na primeira requisição de
    // documento. LocalFileStorage usa a mesma validação (FileStorageKey.Parse).
    _ = FileStorageKey.Parse(builder.Configuration[FileStorageKey.ConfigurationKey]);
    builder.Services.AddScoped<IFileStorage, LocalFileStorage>();
    builder.Services.AddScoped<StorageKeyRotation>();
    builder.Services.AddScoped<IDocumentService, DocumentService>();

    // Email (Spec 80)
    builder.Services.AddScoped<IEmailSender, ConsoleEmailSender>();
    // C9: o envio sai do escopo da requisição — fila em memória + background service com escopo
    // próprio e retry limitado. Sem isto, o e-mail era disparado numa task descartada que usava
    // serviços scoped depois da resposta, e a falha virava warning no log.
    builder.Services.AddCicloEmailDispatch();

    // Notification services (Spec 80)
    builder.Services.AddScoped<INotificationService, NotificationService>();

    // Parent Portal services (Spec 90)
    builder.Services.AddScoped<IParentService, ParentService>();

    // Rate limiting (Spec 30) — políticas aplicadas por atributo nos endpoints anônimos.
    builder.Services.AddCicloRateLimiting();

    var app = builder.Build();

    // Global exception handling (Spec 90)
    app.UseMiddleware<ExceptionMiddleware>();

    // Middleware pipeline
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseSerilogRequestLogging();

    // Rate limiting (Spec 30) — protege os endpoints anônimos de força bruta e varredura.
    // Fica antes da autenticação de propósito: rejeitar cedo evita gastar trabalho com quem
    // está abusando. As políticas são declaradas nos próprios endpoints ([EnableRateLimiting]).
    app.UseRateLimiter();

    // Auth pipeline (Spec 30)
    app.UseAuthentication();
    app.UseMiddleware<TenantMiddleware>();     // Spec 20 — tenant resolution from JWT
    app.UseMiddleware<AuditContextMiddleware>(); // Spec 110 — ator, origem e correlação
    app.UseAuthorization();

    app.MapControllers();

    // ── Schema e dados de demonstração — decisão A1 ──────────────────────────────
    // Plano: management/cross-session/plano-correcoes.md (A1). Estratégia híbrida:
    //   Development → a API aplica as migrations sozinha (schema em dia, sem passo manual).
    //   Outros ambientes → a API NÃO aplica: o deploy roda `dotnet ef database update` antes de
    //     subir. Em troca, o processo em runtime não precisa de permissão de DDL, duas instâncias
    //     não disputam a migration, e uma migration problemática não impede a API de subir.
    //   Testing → nada. Antes o host de teste rodava como Development e portanto mexia no banco de
    //     desenvolvimento a cada execução da suíte. Ver ApiTestHost.
    if (app.Environment.IsDevelopment())
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var startupLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        // Fail-closed. Antes isto era EnsureCreatedAsync dentro de um catch que só logava warning:
        // a API subia com banco vazio e falhava em toda consulta, sem sinal claro na inicialização.
        // EnsureCreated também nunca alterava tabela existente — era o defeito do item A1.
        await db.Database.MigrateAsync();

        await TenantSeeder.SeedAsync(db, app.Environment, startupLogger);
        await DemoDataSeeder.SeedAsync(scope.ServiceProvider, db, app.Environment, startupLogger);
    }
    else if (!app.Environment.IsEnvironment("Testing"))
    {
        // Fail-closed: se o passo explícito do deploy não rodou, não subir é melhor do que servir
        // erro em toda requisição. A verificação apenas LÊ o histórico de migrations — não exige
        // permissão de DDL.
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();

        if (pending.Count > 0)
        {
            throw new InvalidOperationException(
                $"The database schema is out of date: {pending.Count} pending migration(s) "
                + $"({string.Join(", ", pending)}). Run 'dotnet ef database update' before starting the API.");
        }
    }

    // ── Manutenção: rotação da chave de armazenamento ────────────────────────────
    // Uso: dotnet run --project src/Ciclo.Api -- --rotate-storage-key
    // Re-encripta todos os documentos com a chave ATUAL e encerra (não sobe o servidor).
    // O procedimento completo está no README, seção "Rotação de segredos".
    if (args.Contains("--rotate-storage-key"))
    {
        using var rotationScope = app.Services.CreateScope();
        var rotation = rotationScope.ServiceProvider.GetRequiredService<StorageKeyRotation>();
        var rotationLogger = rotationScope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        var result = await rotation.RotateAsync();

#pragma warning disable CA1848, CA1873
        rotationLogger.LogInformation(
            "Storage key rotation finished: {Rotated} re-encrypted, {Missing} missing, {Failed} failed.",
            result.Rotated, result.MissingFiles, result.Failed);
#pragma warning restore CA1848, CA1873

        // Exit code não-zero quando algo ficou para trás: um script de operação não pode
        // interpretar uma rotação parcial como sucesso.
        Environment.ExitCode = result.Failed > 0 ? 1 : 0;
        return;
    }

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    // Fail-closed: uma inicialização que falha (por exemplo, segredo obrigatório ausente)
    // precisa ser observável como falha — sem isso um script de deploy leria o processo
    // como bem-sucedido.
    //
    // HostAbortedException é excluída de propósito: é o mecanismo NORMAL das ferramentas de
    // design-time do EF (elas abortam o host de propósito, depois de capturá-lo). Sem esta guarda,
    // todo `dotnet ef` imprime um FTL "Application terminated unexpectedly" e um agente futuro
    // persegue uma falha que não existe.
    Log.Fatal(ex, "Application terminated unexpectedly");
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program { }
