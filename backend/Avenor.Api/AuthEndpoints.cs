using System.Security.Claims;
using Avenor.Application;
using Avenor.Domain;
using Avenor.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Avenor.Api;

public static class AuthEndpoints
{
    public static void WriteCsrf(HttpContext ctx, IAntiforgery csrf)
    {
        var tokens = csrf.GetAndStoreTokens(ctx);
        ctx.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions { HttpOnly = false, SameSite = SameSiteMode.Strict, Secure = ctx.Request.IsHttps, Path = "/" });
    }

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/auth/csrf", (HttpContext ctx, IAntiforgery csrf) =>
        {
            var tokens = csrf.GetAndStoreTokens(ctx);
            ctx.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions { HttpOnly = false, SameSite = SameSiteMode.Strict, Secure = ctx.Request.IsHttps, Path = "/" });
            return Results.Ok(new
            {
                token = tokens.RequestToken
            });
        });
        app.MapPost("/api/auth/login", async (LoginRequest request, HttpContext ctx, AvenorDbContext db, IPasswordHasher<SupportUser> hasher, IAntiforgery csrf, LoginProbe loginProbe) =>
        {
            var errors = DtoValidation.Validate(request);
            if (errors.Count > 0)
                return Results.ValidationProblem(errors);
            var slug = request.OrganizationSlug.Trim().ToLowerInvariant();
            var email = request.Email.Trim().ToLowerInvariant();
            var org = await db.Organizations.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Slug == slug);
            var user = org is null ? null : await db.Users.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.TenantId == org.Id && x.Email == email && x.IsActive);
            // Do a password hash verification even for unknown accounts to reduce timing differences.
            var probe = user ?? new SupportUser();
            var hash = user?.PasswordHash ?? loginProbe.Hash;
            var valid = hasher.VerifyHashedPassword(probe, hash, request.Password);
            if (user is null || org is null || valid == PasswordVerificationResult.Failed)
                return Results.Problem(statusCode: 401, title: "Unable to sign in", detail: "Check your workspace, email and password.");
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim("tenant", user.TenantId.ToString()), new Claim(ClaimTypes.Name, user.Name), new Claim(ClaimTypes.Role, user.Role.ToString()) }, CookieAuthenticationDefaults.AuthenticationScheme));
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties { IsPersistent = false });
            ctx.User = principal;
            WriteCsrf(ctx, csrf);
            return Results.Ok(user.ToDto(org));
        }).RequireRateLimiting("login");
        app.MapGet("/api/auth/me", async (AvenorDbContext db, ITenantContext tenant) =>
        {
            var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == tenant.UserId);
            var org = await db.Organizations.AsNoTracking().SingleAsync();
            return Results.Ok(user.ToDto(org));
        }).RequireAuthorization();
        app.MapPost("/api/auth/logout", async (HttpContext ctx) => { await ctx.SignOutAsync(); ctx.Response.Cookies.Delete("XSRF-TOKEN"); return Results.NoContent(); }).RequireAuthorization();
    }
}
