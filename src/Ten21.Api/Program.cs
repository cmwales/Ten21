using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Ten21.Api.ExceptionHandling;
using Ten21.Api.Filters;
using Ten21.Domain.Common;
using Ten21.Infrastructure;
using Ten21.Infrastructure.Authorization;
using Ten21.Infrastructure.Email;
using Ten21.Infrastructure.Identity;
using Ten21.Infrastructure.Import;
using Ten21.Infrastructure.Middleware;
using Ten21.Infrastructure.Persistence;
using Ten21.Infrastructure.RateLimiting;
using Ten21.Infrastructure.Security;
using Ten21.Infrastructure.Storage;

var builder = WebApplication.CreateBuilder(args);

// US-08: ApiResponseWrappingFilter auto-wraps every 2xx response in the standard envelope
// -- registered as a global MVC filter rather than left for each controller to opt into.
//
// JsonStringEnumConverter: without this, System.Text.Json's default enum handling expects
// the numeric underlying value (0, 1, 2...) on the wire, not the name -- silently rejecting
// every request that sends an enum as a string ("SingleFamily", "Vacant") with a 400, which
// is exactly what the Angular frontend (and any real client) naturally sends. This bug went
// undetected through Sprint 3's entire test suite because unit tests call controller
// actions directly (never through JSON model binding) and no integration test exercised
// PropertiesController over real HTTP -- only caught by a live browser test against a real
// backend. Global, not per-controller: every current and future enum-typed contract field
// benefits uniformly, and it's what the frontend already assumes everywhere. Sourced from
// Ten21JsonOptions rather than `new JsonStringEnumConverter()` inline so this registration
// can never silently drift from AuditSaveChangesInterceptor's JSON.Serialize calls, which
// use the same shared options for exactly this reason.
builder.Services.AddControllers(options => options.Filters.Add<ApiResponseWrappingFilter>())
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(Ten21JsonOptions.CreateEnumConverter()));

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddObjectStorage(builder.Configuration); // US-06
builder.Services.AddBotDefense(); // US-18
builder.Services.AddEmail(builder.Configuration); // US-16
builder.Services.AddInputSanitization(); // US-19
builder.Services.AddPropertyImport(); // US-21
builder.Services.AddEndpointsApiExplorer();

// US-00: Swagger UI at /swagger with a JWT Bearer authorization header so
// endpoints behind [Authorize] can be exercised interactively in dev.
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Ten21 API", Version = "v1" });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the access token returned by POST /api/auth/login (no \"Bearer \" prefix needed).",
    });

    options.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer", null),
            new List<string>()
        },
    });
});

// US-09: the only registered IExceptionHandler -- every unhandled exception, domain or
// otherwise, funnels through GlobalExceptionHandler and comes out as RFC 7807
// ProblemDetails. AddProblemDetails() wires the ProblemDetails writer infrastructure that
// AddExceptionHandler builds on.
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// US-05: 5 requests/minute per client IP on /api/auth/* (applied via [EnableRateLimiting]
// on AuthController). See AuthRateLimiterPolicy for why this uses a partitioned limiter
// rather than the simpler global-bucket overload.
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy(AuthRateLimiterPolicy.PolicyName, AuthRateLimiterPolicy.GetPartition);

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { message = "Too many requests. Please try again shortly." },
            cancellationToken: cancellationToken);
    };
});

// JWT Bearer authentication SCHEME configuration lives here (Api), not in Infrastructure's
// AddInfrastructure() -- token issuance/persistence is a layer-agnostic concern
// (Infrastructure), but how THIS specific host validates incoming bearer tokens on the
// request pipeline is host-specific wiring that belongs with Program.cs.
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException(
        "Jwt:Key is not configured. Set it via `dotnet user-secrets set \"Jwt:Key\" \"<value>\"` " +
        "in src/Ten21.Api -- see README.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero, // 15-minute tokens don't need grace-period slack
        };
    });

// US-03: registers one [Authorize(Policy = Permissions.X.Y)] policy per permission
// constant, the additive-claims transformation, and the Tenant hard-block handler. Also
// carries the secure-by-default fallback policy (moved here from US-01/US-02) -- see the
// comment on AddTen21Authorization for why policy SHAPE belongs in Infrastructure rather
// than Program.cs.
builder.Services.AddTen21Authorization();

var app = builder.Build();

// US-09's exception handler goes FIRST in the pipeline -- it needs to be able to catch
// exceptions thrown by any middleware that runs after it, not just controller actions.
app.UseExceptionHandler();

// Dev-only auto-migration: `docker compose up` + `dotnet run` is the entire local setup,
// no manual `dotnet ef database update` step to remember.
//
// Deliberately gated to Development and nothing else. In any other environment (staging,
// production), running migrations as a side effect of the app booting is a real footgun
// the moment you run more than one instance -- concurrent instances would race to alter
// the same schema simultaneously, with no review step before a change hits real tenant
// data. Production migrations belong in a single, explicit, reviewed CI/CD deploy step
// (see .gitlab-ci.yml), never here. Role seeding follows the exact same reasoning.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(); // serves the interactive UI at /swagger

    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<Ten21DbContext>();
    await db.Database.MigrateAsync();
    // The RLS policies in sql/rls-policies.sql are meant to be folded into the migration
    // itself (via migrationBuilder.Sql(...) in its Up() method) once you generate it with
    // `dotnet ef migrations add` -- see that file's header comment for exactly where they
    // go. Once that's done, MigrateAsync() above applies schema AND row-level security in
    // the same call, and EF's migration history table keeps it from re-running on every
    // restart.

    // DevSeeder retired as of US-14: POST /api/auth/register is now the real, self-service
    // way to get a usable account on a fresh database -- see User_Stories_Phase_5.md.
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
    await RoleSeeder.SeedAsync(roleManager);
}

// TenantMiddleware reads claims off HttpContext.User, which UseAuthentication() populates
// from the bearer token -- so authentication must run first.
app.UseAuthentication();
app.UseTenantResolution();

app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.Run();

// Exposed so WebApplicationFactory<Program>-based integration tests (US-01's "xUnit
// integration tests prove queries strictly restrict records to active tenant") can spin
// up this host in-process.
public partial class Program;
