using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Ciclo.Core.Entities;
using Ciclo.Infrastructure;
using Ciclo.Infrastructure.Auth;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Services;
using Ciclo.Infrastructure.Storage;
using Ciclo.Infrastructure.Tenancy;
using Ciclo.Infrastructure.Email;
using Ciclo.Api.Middleware;
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
    var jwtSecret = builder.Configuration["Jwt:Secret"]
        ?? throw new InvalidOperationException("Jwt:Secret is not configured");

    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew = TimeSpan.Zero
        };
    });

    // Google OAuth (only if configured)
    var googleClientId = builder.Configuration["Google:ClientId"];
    if (!string.IsNullOrEmpty(googleClientId))
    {
        builder.Services.AddAuthentication().AddGoogle(options =>
        {
            options.ClientId = googleClientId;
            options.ClientSecret = builder.Configuration["Google:ClientSecret"] ?? string.Empty;
        });
    }

    // Auth services (Spec 30)
    builder.Services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
    builder.Services.AddScoped<IAuthService, AuthService>();

    // Tenant services (school registration)
    builder.Services.AddScoped<ITenantService, TenantService>();

    // Student services (Spec 40)
    builder.Services.AddScoped<IStudentService, StudentService>();

    // Enrollment services (Spec 50)
    builder.Services.AddScoped<IEnrollmentService, EnrollmentService>();

    // Reenrollment services (Spec 60)
    builder.Services.AddScoped<IReenrollmentService, ReenrollmentService>();

    // Document services (Spec 70)
    builder.Services.Configure<FileStorageOptions>(builder.Configuration.GetSection(FileStorageOptions.SectionName));
    builder.Services.AddScoped<IFileStorage, LocalFileStorage>();
    builder.Services.AddScoped<IDocumentService, DocumentService>();

    // Email (Spec 80)
    builder.Services.AddScoped<IEmailSender, ConsoleEmailSender>();

    // Notification services (Spec 80)
    builder.Services.AddScoped<INotificationService, NotificationService>();

    // Parent Portal services (Spec 90)
    builder.Services.AddScoped<IParentService, ParentService>();

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

    // Auth pipeline (Spec 30)
    app.UseAuthentication();
    app.UseMiddleware<TenantMiddleware>();     // Spec 20 — tenant resolution from JWT
    app.UseAuthorization();

    app.MapControllers();

    // Create schema (EnsureCreated) and seed in Development
    if (app.Environment.IsDevelopment())
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        try
        {
            await db.Database.EnsureCreatedAsync();
            await TenantSeeder.SeedAsync(db, app.Environment,
                scope.ServiceProvider.GetRequiredService<ILogger<Program>>());
            await DemoDataSeeder.SeedAsync(scope.ServiceProvider, db, app.Environment,
                scope.ServiceProvider.GetRequiredService<ILogger<Program>>());
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Database schema creation failed — database may not be available");
        }
    }

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program { }
