using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Messenger.Api.Auth;
using Messenger.Api.Hubs;
using Messenger.Api.Middleware;
using Messenger.Application.Services;
using Messenger.Infrastructure;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Structured logging first so startup failures are captured.
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .CreateLogger();
builder.Host.UseSerilog();

// Development convenience: an ephemeral signing key so `dotnet run` works with
// zero secrets. Anything shared (staging/prod/tests) must set Jwt:SigningKey.
if (builder.Environment.IsDevelopment()
    && string.IsNullOrWhiteSpace(builder.Configuration["Jwt:SigningKey"]))
{
    var devKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Jwt:SigningKey"] = devKey
    });
    Log.Warning("Generated an ephemeral development JWT signing key. Sessions will not survive restarts.");
}

// Composition root: Application (services + validation) + Infrastructure (Mongo, clock, ids, auth, push).
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// AuthN/Z: JWT bearer + role policies. Controllers build their caller from claims.
builder.Services.AddMessengerAuthentication();

// Transport + surface.
builder.Services.AddControllers();
builder.Services.AddSignalR();
// Abuse backstop (not a quota): generous per-caller fixed windows. Credential
// endpoints take the stricter "auth" policy at the endpoint level.
// Tunables live under RateLimit:* (tests raise them; production keeps defaults).
// NOTE: configured via IOptions (resolved per-request) rather than an eager
// builder.Configuration read, so late-registered sources (e.g. test overrides)
// are always honored.
builder.Services.AddRateLimiter(_ => { });
builder.Services.AddOptions<RateLimiterOptions>().Configure<IConfiguration>((options, cfg) =>
{
    var section = cfg.GetSection("RateLimit");
    var authPermits = section.GetValue("AuthPermitLimit", 20);
    var apiPermits = section.GetValue("ApiPermitLimit", 600);
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        var user = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? ctx.User.FindFirst("sub")?.Value;
        var key = user is null
            ? $"ip:{ctx.Connection.RemoteIpAddress}"
            : $"user:{user}";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = apiPermits,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
    options.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
        $"auth:{ctx.Connection.RemoteIpAddress}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = authPermits,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { title = "Too many requests. Slow down and retry.", status = 429, code = "rate.limited" },
            cancellationToken: token).ConfigureAwait(false);
    };
});// Scaling: single instance needs no backplane (all hub traffic is group-based,
// so it is backplane-compatible by construction). When multiple API instances
// sit behind a load balancer, reference
// Microsoft.AspNetCore.SignalR.StackExchangeRedis and chain it here:
//   .AddStackExchangeRedis(redisConnectionString, o => o.Configuration.ChannelPrefix = "messenger");
// Presence counts stay per-instance: use sticky sessions or treat presence as
// best-effort while last-seen remains durable.
builder.Services.AddSingleton<IPresenceTracker, InMemoryPresenceTracker>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "Messenger API", Version = "v1" });
});
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks().AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy());

// SPA dev origin (Vite). Production serves the SPA from the same origin via nginx.
builder.Services.AddCors(options => options.AddPolicy("spa", policy =>
    policy.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:5173"])
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseSerilogRequestLogging();
app.UseCors("spa");
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Liveness (process) vs readiness (dependencies: Mongo ping).
app.MapHealthChecks("/healthz", new() { Predicate = check => check.Name == "self" });
app.MapHealthChecks("/readyz", new() { Predicate = check => check.Tags.Contains("ready") });
app.MapHealthChecks("/health/storage", new() { Predicate = check => check.Tags.Contains("storage") });

// Fail fast on unsafe production configuration (defaults are dev-only).
if (app.Environment.IsProduction())
{
    var minioUser = app.Configuration["Minio:AccessKey"];
    var minioPass = app.Configuration["Minio:SecretKey"];
    if (minioUser == "minioadmin" || minioPass == "minioadmin")
    {
        throw new InvalidOperationException(
            "Production requires non-default MinIO credentials (Minio:AccessKey / Minio:SecretKey).");
    }

    if (string.Equals(app.Configuration["SeedAdmin:Enabled"], "true", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("SeedAdmin must not be enabled in Production.");
    }
}

app.MapControllers();
app.MapHub<MessengerHub>("/hubs/messenger");

app.Run();

// Test hook for WebApplicationFactory.
public partial class Program;
