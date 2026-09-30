using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using NexaFlow.Api.Endpoints;
using NexaFlow.Api.Hubs;
using NexaFlow.Api.Middleware;
using NexaFlow.Application;
using NexaFlow.Application.Abstractions;
using NexaFlow.Infrastructure;
using NexaFlow.Infrastructure.Caching;
using Scalar.AspNetCore;
using Serilog;

// ====================================================================
// NexaFlow — Api Composition Root
// Phase 2 scope: all Phase 1 wiring + JWT bearer auth + auth endpoints
// + tenant resolution middleware. Auth flows live in the AuthController.
// ====================================================================

var builder = WebApplication.CreateBuilder(args);

// --- Serilog structured logging (section 32) ---
builder.Host.UseSerilog((ctx, services, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "NexaFlow.Api")
    .Enrich.WithProperty("Environment", ctx.HostingEnvironment.EnvironmentName)
    .WriteTo.Async(a => a.Console(outputTemplate:
        "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3} {CorrelationId}] " +
        "{SourceContext} {Message:lj}{NewLine}{Exception}"))
);

builder.Services.AddOptions();

// --- Application + Infrastructure ---
builder.Services.AddApplication();
builder.Services.AddAuthOptions(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

// --- Health checks (section 33) ---
builder.Services.AddHealthChecks()
    .AddNpgSql(
        connectionString: builder.Configuration.GetConnectionString("PostgreSQL")
            ?? throw new InvalidOperationException("PostgreSQL connection string is missing."),
        name: "postgres",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready", "postgres"]);

// --- JWT bearer authentication (section 8) ---
// The signing key is loaded from Authentication:JwtSigningKey. NEVER logged.
var authSection = builder.Configuration.GetSection("Authentication");
var signingKey = authSection["JwtSigningKey"]
    ?? throw new InvalidOperationException("Authentication:JwtSigningKey is missing.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            ValidIssuer = authSection["JwtIssuer"],
            ValidAudience = authSection["JwtAudience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            // Sub claim is the user id (Guid).
            NameClaimType = System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub
        };
    });

builder.Services.AddAuthorization();

// --- MVC controllers (section 28) ---
// AddControllers() registers controller services. .NET 10 uses application-part
// discovery to find controllers in the entry assembly automatically.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Use camelCase JSON output (default for ASP.NET Core).
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

// --- OpenAPI (section 41) ---
builder.Services.AddOpenApi();

// --- CORS (Phase 3 will restrict to specific origins) ---
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (corsOrigins.Length == 0)
        {
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
        }
        else
        {
            policy.WithOrigins(corsOrigins).AllowAnyMethod().AllowAnyHeader();
        }
    });
});

// --- Rate limiting (Phase 7: Redis-backed distributed) ---
// The built-in ASP.NET Core rate limiter uses in-memory state per instance.
// For multi-instance deployments, a Redis-backed limiter is required (section 23).
// Implemented as custom middleware instead of fighting the built-in partition API.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, _) =>
    {
        context.HttpContext.Response.Headers["Retry-After"] = "60";
        return new ValueTask();
    };
});

// --- HSTS / HTTPS redirection (section 40 — secure defaults) ---
builder.Services.AddHsts(options =>
{
    options.Preload = true;
    options.IncludeSubDomains = true;
    options.MaxAge = TimeSpan.FromDays(365);
});

// --- Phase 6: SignalR + Notification Pusher ---
builder.Services.AddSignalR();
builder.Services.AddSingleton<INotificationPusher, SignalRNotificationPusher>();

var app = builder.Build();

// --- Middleware pipeline (order matters) ---
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseSerilogRequestLogging();
app.UseMiddleware<TraceIdMiddleware>();
app.UseMiddleware<CurrentUserMiddleware>();

// Authentication must be wired before tenant resolution (the tenant resolver needs
// the authenticated principal to know which user to look up memberships for).
app.UseAuthentication();

// Tenant resolution: validates X-Organization-Id against the user's actual current
// memberships in the database (section 7 — never trust the header alone).
app.UseMiddleware<TenantResolutionMiddleware>();

app.UseAuthorization();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseCors();
app.UseMiddleware<RedisRateLimitMiddleware>();
app.UseRateLimiter();

// --- Endpoints ---
app.MapHealthEndpoints();
app.MapControllers();
app.MapHub<NotificationHub>("/hubs/notifications");

app.Run();

// Make Program class accessible for WebApplicationFactory-based integration tests.
public partial class Program;
