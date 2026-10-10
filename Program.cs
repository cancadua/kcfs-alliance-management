using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using AllianceRewards.Api.Data;
using AllianceRewards.Api.Middleware;
using AllianceRewards.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (jwt.Key.Length < 32)
    throw new InvalidOperationException("Jwt:Key must be configured and at least 32 characters long.");
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = System.Security.Claims.ClaimTypes.Name,
            RoleClaimType = System.Security.Claims.ClaimTypes.Role,
        };
    });
builder.Services.AddAuthorization();

// Allowed origins come from config (Cors:Origins, or Cors__Origins__0 etc. as env vars).
// The Angular dev server is always allowed in Development.
var corsOrigins = (builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .Select(o => o.TrimEnd('/'))
    .Where(o => o.Length > 0)
    .ToList();
if (builder.Environment.IsDevelopment() && !corsOrigins.Contains("http://localhost:4200"))
    corsOrigins.Add("http://localhost:4200");
if (corsOrigins.Count == 0)
    throw new InvalidOperationException("Cors:Origins must list at least one allowed origin (env var Cors__Origins__0).");

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins([.. corsOrigins])
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithExposedHeaders("ETag")));

// Render terminates TLS in a proxy: take the client IP/scheme from X-Forwarded-* (only the last hop, set by
// the proxy), so per-IP rate limits see real clients instead of the proxy.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

// Brute-force protection for register/login: fixed window per client IP.
var authLimit = builder.Configuration.GetValue("RateLimit:Auth:PermitLimit", 20);
var authWindow = TimeSpan.FromSeconds(builder.Configuration.GetValue("RateLimit:Auth:WindowSeconds", 60));
var linkLimit = builder.Configuration.GetValue("RateLimit:Link:PermitLimit", 10);
var linkWindow = TimeSpan.FromSeconds(builder.Configuration.GetValue("RateLimit:Link:WindowSeconds", 300));
var mapCreateLimit = builder.Configuration.GetValue("RateLimit:SharedMapCreate:PermitLimit", 10);
var mapCreateWindow = TimeSpan.FromSeconds(builder.Configuration.GetValue("RateLimit:SharedMapCreate:WindowSeconds", 3600));
var mapLimit = builder.Configuration.GetValue("RateLimit:SharedMap:PermitLimit", 300);
var mapWindow = TimeSpan.FromSeconds(builder.Configuration.GetValue("RateLimit:SharedMap:WindowSeconds", 60));
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = authLimit,
            Window = authWindow,
            QueueLimit = 0,
        }));
    // Link codes and link requests: fixed window per signed-in user, against code guessing and request spam.
    o.AddPolicy("link", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = linkLimit,
            Window = linkWindow,
            QueueLimit = 0,
        }));
    // Anonymous shared maps, per client IP: creating is rare, reading/patching covers polling.
    o.AddPolicy("shared-map-create", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = mapCreateLimit,
            Window = mapCreateWindow,
            QueueLimit = 0,
        }));
    o.AddPolicy("shared-map", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = mapLimit,
            Window = mapWindow,
            QueueLimit = 0,
        }));
});

builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<AllianceAccessService>();
builder.Services.AddScoped<RecommendationService>();
builder.Services.AddScoped<PlayerLinkService>();
builder.Services.AddScoped<SharedMapService>();
builder.Services.AddSingleton<SharedMapCleanupService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SharedMapCleanupService>());

var app = builder.Build();

// Apply pending EF migrations on startup so deploys (Render -> Neon) keep the schema current.
if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

app.UseForwardedHeaders();
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseHttpsRedirection();
}

app.UseCors();
// After authentication, so per-user rate limit policies can see the user.
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();

app.Run();
