using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Avenor.Domain;
using Microsoft.EntityFrameworkCore;

namespace Avenor.Infrastructure;

/// <summary>Privileged, offline provisioning. Never mapped to an HTTP endpoint.</summary>
public static partial class WorkspaceBootstrapper
{
    public static async Task CreateAsync(AvenorDbContext db, string name, string slug, string adminName, string email, string password, Func<SupportUser, string, string> hash, CancellationToken ct = default)
    {
        name = name.Trim();
        slug = slug.Trim().ToLowerInvariant();
        adminName = adminName.Trim();
        email = email.Trim().ToLowerInvariant();
        if (name.Length is < 2 or > 100 || adminName.Length is < 2 or > 80 || !SlugPattern().IsMatch(slug) || email.Length > 254 || !new EmailAddressAttribute().IsValid(email))
            throw new InvalidOperationException("Bootstrap requires a valid workspace name (2–100), slug (3–64 lowercase letters/digits/hyphens), admin name (2–80), and email.");
        if (password.Length is < 16 or > 128 || !password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit) || !password.Any(x => !char.IsLetterOrDigit(x)))
            throw new InvalidOperationException("Bootstrap password requires 16–128 characters with uppercase, lowercase, a number and a symbol.");
        if (await db.Organizations.IgnoreQueryFilters().AnyAsync(x => x.Slug == slug, ct))
            throw new InvalidOperationException("That workspace slug already exists. Bootstrap never resets existing credentials.");
        db.Seeding = true;
        try
        {
            var org = new Organization { Name = name, Slug = slug, AssignmentStrategy = AssignmentStrategy.Manual };
            var user = new SupportUser { TenantId = org.Id, Name = adminName, Email = email, Role = UserRole.Admin, Company = name };
            user.PasswordHash = hash(user, password);
            db.Organizations.Add(org);
            db.Users.Add(user);
            foreach (var p in new[] { (Priority.Urgent, 30, 240), (Priority.High, 120, 480), (Priority.Normal, 480, 1440), (Priority.Low, 1440, 4320) })
                db.SlaPolicies.Add(new SlaPolicy { TenantId = org.Id, Priority = p.Item1, ResponseMinutes = p.Item2, ResolutionMinutes = p.Item3 });
            db.AuditEntries.Add(new AuditEntry { TenantId = org.Id, Action = "WorkspaceCreated", Description = "Workspace and initial administrator provisioned through the offline bootstrap command.", ActorName = "Workspace bootstrap" });
            await db.SaveChangesAsync(ct);
        }
        finally { db.Seeding = false; }
    }
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,62}[a-z0-9]$")]
    private static partial Regex SlugPattern();
}
