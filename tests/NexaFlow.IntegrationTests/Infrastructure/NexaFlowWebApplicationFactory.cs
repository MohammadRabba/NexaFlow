using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NexaFlow.Application.Abstractions;
using NexaFlow.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NexaFlow.IntegrationTests.Infrastructure;

/// <summary>
///     WebApplicationFactory override that:
///     <list type="bullet">
///         <item>Replaces the DbContext connection string with the Testcontainers Postgres one.</item>
///         <item>Re-registers IEmailService as a test fake (so no real emails are sent).</item>
///         <item>Applies the EF Core migrations to ensure the schema is up-to-date before tests run.</item>
///     </list>
/// </summary>
public sealed class NexaFlowWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly PostgreSqlContainer? _postgres;

    public NexaFlowWebApplicationFactory(PostgreSqlContainer? postgres)
    {
        _postgres = postgres;
    }

    public List<string> SentEmailBodies { get; } = [];

    public TestAuthHelper Auth => new(this);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        if (_postgres is null) return;

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseNpgsql(_postgres.GetConnectionString(), npgsql =>
                {
                    npgsql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                });
                options.UseSnakeCaseNamingConvention();
            });

            services.RemoveAll<IEmailService>();
            services.AddSingleton<IEmailService>(new CapturingEmailService(SentEmailBodies));
        });
    }

    public async Task ApplyMigrationsAsync()
    {
        if (_postgres is null) return;
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
    }
}

internal sealed class CapturingEmailService : IEmailService
{
    private readonly List<string> _captured;

    public CapturingEmailService(List<string> captured) => _captured = captured;

    public Task SendAsync(EmailRequest request, CancellationToken cancellationToken = default)
    {
        // Capture the body so tests can extract the verification / reset token.
        // In production this would be a real SMTP send.
        _captured.Add(request.HtmlBody ?? request.TextBody ?? string.Empty);
        return Task.CompletedTask;
    }
}
