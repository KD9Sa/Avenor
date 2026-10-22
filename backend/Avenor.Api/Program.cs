using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Avenor.Api;
using Avenor.Application;
using Avenor.Domain;
using Avenor.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
// Containers and local development log to stdout; no Windows Event Log permissions required.
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<HttpTenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<HttpTenantContext>());
builder.Services.AddSingleton<LiveEvents>();
builder.Services.AddScoped<IPasswordHasher<SupportUser>, PasswordHasher<SupportUser>>();
builder.Services.AddSingleton(new LoginProbe(new PasswordHasher<SupportUser>().HashPassword(new SupportUser(), "invalid-login-probe-Avenor")));
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddDataProtection().SetApplicationName("Avenor").PersistKeysToFileSystem(new DirectoryInfo(builder.Configuration["DataProtection:KeyPath"] ?? Path.Combine(builder.Environment.ContentRootPath, ".local", "keys")));
builder.Services.AddDbContext<AvenorDbContext>(options =>
{
    var connection = builder.Configuration.GetConnectionString("DefaultConnection");
    var provider = builder.Configuration["Database:Provider"];
    if (string.IsNullOrWhiteSpace(connection))
        throw new InvalidOperationException("Configure ConnectionStrings:DefaultConnection. No production database fallback is used.");
    if (provider == "Sqlite")
    {
        if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
            throw new InvalidOperationException("SQLite is permitted only in Development or Testing.");
        options.UseSqlite(connection);
    }
    else if (provider == "SqlServer")
        options.UseSqlServer(connection);
    else
        throw new InvalidOperationException("Database:Provider must be SqlServer or Sqlite.");
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "avenor.session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing") ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = false;
    options.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
    options.Events.OnValidatePrincipal = async ctx =>
    {
        var uid = ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var tid = ctx.Principal?.FindFirstValue("tenant");
        if (!Guid.TryParse(uid, out var userId) || !Guid.TryParse(tid, out var tenantId))
        {
            ctx.RejectPrincipal();
            return;
        }
        var db = ctx.HttpContext.RequestServices.GetRequiredService<AvenorDbContext>();
        // Authentication deliberately bypasses filters, then constrains BOTH validated cookie identifiers.
        var user = await db.Users.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId && x.TenantId == tenantId && x.IsActive);
        if (user is null || user.Role.ToString() != ctx.Principal!.FindFirstValue(ClaimTypes.Role))
            ctx.RejectPrincipal();
    };
});
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Staff", p => p.RequireRole("Admin", "Agent"));
    options.AddPolicy("Admin", p => p.RequireRole("Admin"));
});
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.Name = "avenor.csrf";
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing") ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 15, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddHostedService<EscalationWorker>();
var app = builder.Build();

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    ctx.Response.Headers["Cache-Control"] = "no-store";
    try
    {
        await next();
    }
    catch (DbUpdateConcurrencyException) { await Results.Problem(statusCode: 409, title: "This record has changed", detail: "Refresh the record and apply your changes again.").ExecuteAsync(ctx); }
    catch (RuleViolation e) { await Results.Problem(statusCode: 409, title: "Action unavailable", detail: e.Message).ExecuteAsync(ctx); }
    catch (DbUpdateException e) { app.Logger.LogWarning(e, "Database constraint rejected a write"); await Results.Problem(statusCode: 409, title: "The change could not be saved", detail: "An account or record with these details already exists, or a related record changed. Refresh and try again.").ExecuteAsync(ctx); }
    catch (BadHttpRequestException) { await Results.Problem(statusCode: 400, title: "Invalid request", detail: "The request body or parameters are invalid.").ExecuteAsync(ctx); }
    catch (Exception e) when (!ctx.Response.HasStarted) { app.Logger.LogError(e, "Unhandled request error"); await Results.Problem(statusCode: 500, title: "An unexpected error occurred", detail: "Please retry. If the problem continues, contact your workspace administrator.").ExecuteAsync(ctx); }
});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api") && ctx.Request.Method is not ("GET" or "HEAD" or "OPTIONS"))
    {
        try
        {
            await ctx.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(ctx);
        }
        catch (AntiforgeryValidationException) { await Results.Problem(statusCode: 400, title: "Security token expired", detail: "Refresh the page and try again.").ExecuteAsync(ctx); return; }
    }
    await next();
});

AuthEndpoints.Map(app);
TicketEndpoints.Map(app);
WorkspaceEndpoints.Map(app);
app.MapGet("/api/health", async (AvenorDbContext db, CancellationToken ct) => await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "healthy", database = db.Database.ProviderName }) : Results.Problem(statusCode: 503, title: "Database unavailable"));
app.MapGet("/api/events", async (HttpContext ctx, ITenantContext tenant, LiveEvents events) =>
{
    ctx.Response.ContentType = "text/event-stream";
    ctx.Response.Headers["X-Accel-Buffering"] = "no";
    var subscription = events.Subscribe(tenant.TenantId!.Value);
    try
    {
        await ctx.Response.WriteAsync("retry: 10000\n\n", ctx.RequestAborted);
        await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
        while (!ctx.RequestAborted.IsCancellationRequested)
        {
            using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);
            heartbeat.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                await subscription.Reader.ReadAsync(heartbeat.Token);
                await ctx.Response.WriteAsync("event: update\ndata: {}\n\n", ctx.RequestAborted);
            }
            catch (OperationCanceledException) when (!ctx.RequestAborted.IsCancellationRequested) { await ctx.Response.WriteAsync(": heartbeat\n\n", ctx.RequestAborted); }
            await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
        }
    }
    catch (OperationCanceledException) { }
    finally { events.Unsubscribe(subscription.Id); }
}).RequireAuthorization();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AvenorDbContext>();
    if (db.Database.IsSqlite())
        await db.Database.EnsureCreatedAsync();
    else
        await db.Database.MigrateAsync();
    if (builder.Configuration.GetValue<bool>("bootstrap"))
    {
        if (builder.Configuration.GetValue<bool>("SeedDemo"))
            throw new InvalidOperationException("Disable SeedDemo before provisioning a real workspace.");
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<SupportUser>>();
        await WorkspaceBootstrapper.CreateAsync(db,
            builder.Configuration["Bootstrap:OrganizationName"] ?? "",
            builder.Configuration["Bootstrap:OrganizationSlug"] ?? "",
            builder.Configuration["Bootstrap:AdminName"] ?? "",
            builder.Configuration["Bootstrap:AdminEmail"] ?? "",
            builder.Configuration["Bootstrap:AdminPassword"] ?? "", hasher.HashPassword);
        app.Logger.LogInformation("Workspace provisioning completed. No HTTP server was started.");
    }
    else if (builder.Configuration.GetValue<bool>("SeedDemo"))
    {
        if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
            throw new InvalidOperationException("Demonstration seed is permitted only in Development/Testing.");
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<SupportUser>>();
        await DemoSeeder.SeedAsync(db, hasher.HashPassword);
    }
}
if (builder.Configuration.GetValue<bool>("bootstrap")) { await app.DisposeAsync(); return; }
await app.RunAsync();
public partial class Program;
public sealed record LoginProbe(string Hash);
