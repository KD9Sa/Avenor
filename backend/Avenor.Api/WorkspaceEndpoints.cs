using System.Data;
using System.Text;
using Avenor.Application;
using Avenor.Domain;
using Avenor.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Avenor.Api;

public static class WorkspaceEndpoints
{
    private static bool TryDays(HttpRequest request, out int days) => int.TryParse(request.Query["days"].FirstOrDefault() ?? "7", out days) && days is 7 or 30 or 90;

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/users", async (AvenorDbContext db) =>
        {
            var org = await db.Organizations.AsNoTracking().SingleAsync();
            var users = await db.Users.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
            return Results.Ok(users.Select(x => x.ToDto(org)));
        }).RequireAuthorization("Staff");
        app.MapPost("/api/users", async (CreateUserRequest request, AvenorDbContext db, ITenantContext tenant, IPasswordHasher<SupportUser> hasher, LiveEvents events) =>
        {
            var errors = DtoValidation.Validate(request);
            if (errors.Count > 0)
                return Results.ValidationProblem(errors);
            if (!TicketEndpoints.TryEnum<UserRole>(request.Role, out var role) || request.Name.Trim().Length < 2)
                return Results.Problem(statusCode: 400, title: "Enter a valid name and role");
            if (!request.Password.Any(char.IsUpper) || !request.Password.Any(char.IsLower) || !request.Password.Any(char.IsDigit) || !request.Password.Any(x => !char.IsLetterOrDigit(x)))
                return Results.Problem(statusCode: 400, title: "Use at least 12 characters with upper/lowercase letters, a number and a symbol");
            var email = request.Email.Trim().ToLowerInvariant();
            if (await db.Users.AnyAsync(x => x.Email == email))
                return Results.Problem(statusCode: 409, title: "This email already belongs to a workspace member");
            var user = new SupportUser { TenantId = tenant.TenantId!.Value, Name = request.Name.Trim(), Email = email, Role = role };
            user.PasswordHash = hasher.HashPassword(user, request.Password);
            db.Users.Add(user);
            db.AuditEntries.Add(TicketEndpoints.Audit(tenant, "UserCreated", $"Created {role} account for {user.Name}."));
            await db.SaveChangesAsync();
            events.Publish(user.TenantId);
            return Results.Created($"/api/users/{user.Id}", user.ToDto(await db.Organizations.AsNoTracking().SingleAsync()));
        }).RequireAuthorization("Admin");
        app.MapPatch("/api/users/{id:guid}", async (Guid id, UpdateUserRequest request, AvenorDbContext db, ITenantContext tenant, LiveEvents events) =>
        {
            var errors = DtoValidation.Validate(request);
            if (errors.Count > 0)
                return Results.ValidationProblem(errors);
            if (request.Name is not null && request.Name.Trim().Length < 2)
                return Results.Problem(statusCode: 400, title: "Name must contain at least 2 characters");
            UserRole? role = null;
            if (request.Role is not null)
            {
                if (!TicketEndpoints.TryEnum<UserRole>(request.Role, out var parsed))
                    return Results.Problem(statusCode: 400, title: "Invalid role");
                role = parsed;
            }
            // Serializable protects the last-administrator invariant against concurrent demotions.
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id);
            if (user is null)
                return Results.NotFound();
            if (user.Role == UserRole.Admin && user.IsActive && (role is not null && role != UserRole.Admin || request.IsActive == false) && await db.Users.CountAsync(x => x.Role == UserRole.Admin && x.IsActive) <= 1)
                return Results.Problem(statusCode: 409, title: "Keep at least one active administrator");
            if ((role == UserRole.Customer || request.IsActive == false) && await db.Tickets.AnyAsync(x => x.AssigneeId == id && x.Status != TicketStatus.Resolved && x.Status != TicketStatus.Closed))
                return Results.Problem(statusCode: 409, title: "Reassign this member's active tickets first");
            if (request.Name is not null)
                user.Name = request.Name.Trim();
            if (role is not null)
                user.Role = role.Value;
            if (request.IsActive is not null)
                user.IsActive = request.IsActive.Value;
            db.AuditEntries.Add(TicketEndpoints.Audit(tenant, "UserUpdated", $"Updated account for {user.Name}: {user.Role}, {(user.IsActive ? "active" : "inactive")}."));
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            events.Publish(user.TenantId);
            return Results.Ok(user.ToDto(await db.Organizations.AsNoTracking().SingleAsync()));
        }).RequireAuthorization("Admin");
        app.MapGet("/api/customers", async (AvenorDbContext db) =>
        {
            var customers = await db.Users.AsNoTracking().Where(x => x.Role == UserRole.Customer).OrderBy(x => x.Name).Select(x => new { x.Id, x.Name, x.Email, x.Company, TicketCount = db.Tickets.Count(t => t.CustomerId == x.Id), OpenTicketCount = db.Tickets.Count(t => t.CustomerId == x.Id && t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed) }).ToListAsync();
            return Results.Ok(customers);
        }).RequireAuthorization("Staff");
        app.MapGet("/api/dashboard", async (HttpRequest request, AvenorDbContext db) =>
        {
            if (!TryDays(request, out var days))
                return Results.Problem(statusCode: 400, title: "Choose a 7, 30 or 90 day period");
            var now = DateTime.UtcNow;
            var start = now.Date.AddDays(1 - days);
            var period = await db.Tickets.AsNoTracking().Where(x => x.CreatedAt >= start).ToListAsync();
            var active = await db.Tickets.AsNoTracking().Where(x => x.Status != TicketStatus.Resolved && x.Status != TicketStatus.Closed).Select(x => new { x.Id, x.Status, x.AssigneeId, x.FirstRespondedAt, x.ResponseDueAt, x.ResolutionDueAt }).ToListAsync();
            var responded = period.Where(x => x.FirstRespondedAt is not null).ToArray();
            var eligible = period.Where(x => x.FirstRespondedAt is not null || x.ResponseDueAt < now).ToArray();
            var compliant = eligible.Count(x => x.FirstRespondedAt is { } response && response <= x.ResponseDueAt && (x.ResolvedAt is { } resolved ? resolved <= x.ResolutionDueAt : x.ResolutionDueAt >= now));
            var users = await db.Users.AsNoTracking().Where(x => x.Role != UserRole.Customer && x.IsActive).OrderBy(x => x.Name).ToListAsync();
            var recent = await TicketEndpoints.WithPeople(db.Tickets.AsNoTracking()).Where(x => x.Status != TicketStatus.Resolved && x.Status != TicketStatus.Closed).OrderByDescending(x => x.Priority).ThenBy(x => x.ResponseDueAt).Take(5).ToListAsync();
            var result = new
            {
                totalTickets = period.Count,
                openTickets = active.Count,
                resolvedTickets = period.Count(x => !TicketRules.IsActive(x.Status)),
                pendingTickets = active.Count(x => x.Status == TicketStatus.Pending),
                unassignedTickets = active.Count(x => x.AssigneeId is null),
                overdueTickets = active.Count(x => x.FirstRespondedAt is null && x.ResponseDueAt < now || x.ResolutionDueAt < now),
                slaCompliance = eligible.Length == 0 ? 100 : Math.Round(100d * compliant / eligible.Length, 1),
                averageResponseMinutes = responded.Length == 0 ? 0 : Math.Round(responded.Average(x => (x.FirstRespondedAt!.Value - x.CreatedAt).TotalMinutes), 1),
                volume = Enumerable.Range(0, days).Select(i => { var date = start.AddDays(i); return new { date = date.ToString("yyyy-MM-dd"), created = period.Count(x => x.CreatedAt.Date == date), resolved = period.Count(x => x.ResolvedAt?.Date == date) }; }),
                byStatus = Enum.GetValues<TicketStatus>().Select(s => new { status = s.ToString(), count = period.Count(x => x.Status == s) }),
                byCategory = period.GroupBy(x => x.Category).Select(g => new { category = g.Key, count = g.Count() }).OrderByDescending(x => x.count),
                team = users.Select(u => new { u.Id, u.Name, role = u.Role.ToString(), activeTickets = active.Count(x => x.AssigneeId == u.Id), resolvedTickets = period.Count(x => x.AssigneeId == u.Id && !TicketRules.IsActive(x.Status)) }),
                recentTickets = recent.Select(x => x.ToDto())
            };
            return Results.Ok(result);
        }).RequireAuthorization("Staff");
        app.MapGet("/api/reports/export", async (HttpRequest request, AvenorDbContext db) =>
        {
            if (!TryDays(request, out var days))
                return Results.Problem(statusCode: 400, title: "Choose a 7, 30 or 90 day period");
            var start = DateTime.UtcNow.Date.AddDays(1 - days);
            var tickets = await TicketEndpoints.WithPeople(db.Tickets.AsNoTracking()).Where(x => x.CreatedAt >= start).OrderByDescending(x => x.CreatedAt).Take(10000).ToListAsync();
            var csv = new StringBuilder("Number,Subject,Status,Priority,Customer,Assignee,Created UTC,Resolved UTC\r\n");
            foreach (var t in tickets)
                csv.AppendLine(string.Join(',', new[] { t.Number.ToString(), t.Subject, t.Status.ToString(), t.Priority.ToString(), t.Customer.Name, t.Assignee?.Name ?? "Unassigned", t.CreatedAt.ToString("O"), t.ResolvedAt?.ToString("O") ?? "" }.Select(Csv)));
            return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv; charset=utf-8", $"avenor-tickets-{days}days.csv");
        }).RequireAuthorization("Staff");
        app.MapGet("/api/settings", async (AvenorDbContext db) => Results.Ok(await Settings(db))).RequireAuthorization();
        app.MapPut("/api/settings", async (SettingsDto request, AvenorDbContext db, ITenantContext tenant, LiveEvents events) =>
        {
            if (string.IsNullOrWhiteSpace(request.OrganizationName) || request.OrganizationName.Trim().Length is < 2 or > 100 || !TicketEndpoints.TryEnum<AssignmentStrategy>(request.AssignmentStrategy, out var strategy))
                return Results.Problem(statusCode: 400, title: "Enter a workspace name and valid assignment strategy");
            if (request.SlaPolicies is null || request.SlaPolicies.Length != 4 || request.SlaPolicies.Any(x => x is null) || request.SlaPolicies.Select(x => x.Priority).Distinct().Count() != 4 || request.SlaPolicies.Any(x => !TicketEndpoints.TryEnum<Priority>(x.Priority, out _) || x.ResponseMinutes is < 1 or > 43200 || x.ResolutionMinutes < x.ResponseMinutes || x.ResolutionMinutes > 43200))
                return Results.Problem(statusCode: 400, title: "Provide one SLA policy per priority with positive response/resolution times, up to 30 days; resolution must follow response");
            if (string.IsNullOrWhiteSpace(request.BusinessHours) || request.BusinessHours.Length > 100)
                return Results.Problem(statusCode: 400, title: "Business hours label must contain 1–100 characters");
            var org = await db.Organizations.SingleAsync();
            if (request.OrganizationSlug != org.Slug)
                return Results.Problem(statusCode: 400, title: "Workspace slug cannot be changed");
            org.Name = request.OrganizationName.Trim();
            org.AssignmentStrategy = strategy;
            org.BusinessHours = request.BusinessHours.Trim();
            org.Version++;
            var policies = await db.SlaPolicies.ToListAsync();
            foreach (var policy in request.SlaPolicies)
            {
                var entity = policies.Single(x => x.Priority.ToString() == policy.Priority);
                entity.ResponseMinutes = policy.ResponseMinutes;
                entity.ResolutionMinutes = policy.ResolutionMinutes;
            }
            db.AuditEntries.Add(TicketEndpoints.Audit(tenant, "SettingsUpdated", $"Updated workspace settings and {strategy} assignment. SLA updates apply to new tickets; deadlines use elapsed time."));
            await db.SaveChangesAsync();
            events.Publish(org.Id);
            return Results.Ok(await Settings(db));
        }).RequireAuthorization("Admin");
        app.MapGet("/api/audit", async (HttpRequest request, AvenorDbContext db) =>
        {
            if (!int.TryParse(request.Query["page"].FirstOrDefault() ?? "1", out var page) || page < 1 || !int.TryParse(request.Query["pageSize"].FirstOrDefault() ?? "20", out var pageSize) || pageSize is < 1 or > 100)
                return Results.Problem(statusCode: 400, title: "Invalid pagination");
            if ((long)(page - 1) * pageSize > int.MaxValue)
                return Results.Problem(statusCode: 400, title: "Page is out of range");
            var total = await db.AuditEntries.CountAsync();
            var entries = await db.AuditEntries.AsNoTracking().OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return Results.Ok(new PageDto<AuditDto>(entries.Select(x => x.ToDto()).ToArray(), total, page, pageSize));
        }).RequireAuthorization("Admin");
        app.MapGet("/api/notifications", async (AvenorDbContext db, ITenantContext tenant) =>
        {
            var items = await db.Notifications.AsNoTracking().Where(x => x.UserId == tenant.UserId).OrderByDescending(x => x.CreatedAt).Take(100).Select(x => new { x.Id, x.Title, x.Body, x.TicketId, x.IsRead, x.CreatedAt }).ToListAsync();
            return Results.Ok(items);
        }).RequireAuthorization();
        app.MapPost("/api/notifications/read", async (MarkReadRequest request, AvenorDbContext db, ITenantContext tenant, LiveEvents events) =>
        {
            if (request.Ids is null || request.Ids.Length > 100)
                return Results.Problem(statusCode: 400, title: "Provide up to 100 notification IDs");
            var items = await db.Notifications.Where(x => x.UserId == tenant.UserId && request.Ids.Contains(x.Id)).ToListAsync();
            foreach (var item in items)
                item.IsRead = true;
            await db.SaveChangesAsync();
            events.Publish(tenant.TenantId!.Value);
            return Results.NoContent();
        }).RequireAuthorization();
    }

    private static async Task<SettingsDto> Settings(AvenorDbContext db)
    {
        var org = await db.Organizations.AsNoTracking().SingleAsync();
        var policies = await db.SlaPolicies.AsNoTracking().OrderByDescending(x => x.Priority).ToListAsync();
        return new SettingsDto(org.Name, org.Slug, org.AssignmentStrategy.ToString(), policies.Select(x => new SlaPolicyDto(x.Priority.ToString(), x.ResponseMinutes, x.ResolutionMinutes)).ToArray(), org.BusinessHours);
    }
    internal static string Csv(string value)
    {
        if (value.TrimStart().StartsWith('=') || value.TrimStart().StartsWith('+') || value.TrimStart().StartsWith('-') || value.TrimStart().StartsWith('@') || value.StartsWith('\t') || value.StartsWith('\r'))
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
