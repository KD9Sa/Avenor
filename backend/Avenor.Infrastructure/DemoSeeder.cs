using System.Text.Json;
using Avenor.Domain;
using Microsoft.EntityFrameworkCore;

namespace Avenor.Infrastructure;

public static class DemoSeeder
{
    public const string Password = "AvenorDemo!2026";

    public static async Task SeedAsync(AvenorDbContext db, Func<SupportUser, string, string> hash, CancellationToken ct = default)
    {
        if (await db.Organizations.IgnoreQueryFilters().AnyAsync(ct))
            return;
        db.Seeding = true;
        try
        {
            var northstar = new Organization { Name = "Northstar Studio", Slug = "northstar", AssignmentStrategy = AssignmentStrategy.LeastBusy, NextTicketNumber = 1087 };
            var meridian = new Organization { Name = "Meridian Labs", Slug = "meridian", NextTicketNumber = 1042 };
            db.Organizations.AddRange(northstar, meridian);
            var users = new[] {
                User(northstar.Id, "Alex Morgan", "alex@northstar.demo", UserRole.Admin, "Northstar Studio", "#3659E3"),
                User(northstar.Id, "Jordan Lee", "jordan@northstar.demo", UserRole.Agent, "Northstar Studio", "#187653"),
                User(northstar.Id, "Emma Wilson", "emma@northstar.demo", UserRole.Agent, "Northstar Studio", "#94611D"),
                User(northstar.Id, "Sam Rivera", "sam@northstar.demo", UserRole.Agent, "Northstar Studio", "#8057B2"),
                User(northstar.Id, "Morgan Chen", "morgan@acme.demo", UserRole.Customer, "Acme Inc.", "#187653"),
                User(northstar.Id, "Olivia Rhye", "olivia@layers.demo", UserRole.Customer, "Layers", "#8057B2"),
                User(northstar.Id, "James Carter", "james@catalog.demo", UserRole.Customer, "Catalog", "#94611D"),
                User(northstar.Id, "Sophie Moore", "sophie@circooles.demo", UserRole.Customer, "Circooles", "#387E9E"),
                User(meridian.Id, "Meridian Admin", "admin@meridian.demo", UserRole.Admin, "Meridian Labs", "#3659E3"),
                User(meridian.Id, "Meridian Customer", "customer@meridian.demo", UserRole.Customer, "Meridian Labs", "#187653")
            };
            foreach (var user in users)
                user.PasswordHash = hash(user, Password);
            db.Users.AddRange(users);
            foreach (var org in new[] { northstar, meridian })
                foreach (var policy in new[] { (Priority.Urgent, 30, 240), (Priority.High, 120, 480), (Priority.Normal, 480, 1440), (Priority.Low, 1440, 4320) })
                    db.SlaPolicies.Add(new SlaPolicy { TenantId = org.Id, Priority = policy.Item1, ResponseMinutes = policy.Item2, ResolutionMinutes = policy.Item3 });
            var subjects = new[] {
                "Unable to access billing dashboard", "How do I invite new team members?", "API integration returning a 403 error", "Update company details on invoice", "Exported report is missing recent activity", "Single sign-on configuration assistance", "Webhook deliveries are delayed", "Request to upgrade our workspace plan", "Dashboard loading slowly on Safari", "Reset two-factor authentication", "Custom domain verification pending", "Question about annual billing", "CSV import shows duplicate contacts", "Notification preferences are not saving", "Help configuring the Slack integration", "Unable to download last month's invoice", "Search results not matching ticket subject", "Change workspace administrator", "Mobile layout issue on ticket details", "Clarification on data retention policy", "Custom report date range question", "Profile picture upload trouble", "Workspace invitation link expired"
            };
            var now = DateTime.UtcNow;
            for (var i = 0; i < 46; i++)
            {
                var customer = users[4 + i % 4];
                var agent = users[1 + i % 3];
                var created = now.AddHours(-(i * 4.2 + 0.2));
                var status = (i % 10) switch
                {
                    0 => TicketStatus.Open,
                    1 => TicketStatus.InProgress,
                    2 => TicketStatus.Open,
                    3 => TicketStatus.Pending,
                    4 => TicketStatus.InProgress,
                    5 => TicketStatus.Open,
                    6 => TicketStatus.Resolved,
                    7 => TicketStatus.Resolved,
                    8 => TicketStatus.Closed,
                    _ => TicketStatus.Resolved
                };
                var priority = i % 7 == 0 ? Priority.Urgent : i % 3 == 0 ? Priority.High : i % 5 == 0 ? Priority.Low : Priority.Normal;
                var response = priority switch
                {
                    Priority.Urgent => 30,
                    Priority.High => 120,
                    Priority.Low => 1440,
                    _ => 480
                };
                var resolution = priority switch
                {
                    Priority.Urgent => 240,
                    Priority.High => 480,
                    Priority.Low => 4320,
                    _ => 1440
                };
                var ticket = new Ticket
                {
                    TenantId = northstar.Id,
                    Number = 1086 - i,
                    Subject = subjects[i % subjects.Length],
                    Description = $"Hi team, we need some help with {subjects[i % subjects.Length].ToLowerInvariant()}. This is affecting our team's daily workflow. Could you please take a look and let us know the next steps? Thank you!",
                    Status = status,
                    Priority = priority,
                    Category = new[] { "Billing", "General", "Technical", "Billing", "Product", "Account" }[i % 6],
                    Channel = (TicketChannel)(i % 3),
                    CustomerId = customer.Id,
                    AssigneeId = i % 6 == 0 ? null : agent.Id,
                    CreatedAt = created,
                    UpdatedAt = created.AddMinutes(i % 3 == 0 ? 3 : 15),
                    ResponseDueAt = created.AddMinutes(response),
                    ResolutionDueAt = created.AddMinutes(resolution),
                    FirstRespondedAt = i % 3 == 0 ? null : created.AddMinutes(12 + i % 40),
                    ResolvedAt = status is TicketStatus.Resolved or TicketStatus.Closed ? created.AddMinutes(90 + i * 3) : null,
                    TagsJson = JsonSerializer.Serialize(new[] { i % 2 == 0 ? "priority-customer" : "needs-review", new[] { "billing", "onboarding", "api", "invoice", "reports", "sso" }[i % 6] })
                };
                db.Tickets.Add(ticket);
                db.Messages.Add(new TicketMessage { TenantId = northstar.Id, TicketId = ticket.Id, AuthorId = customer.Id, Body = ticket.Description, CreatedAt = created });
                if (ticket.FirstRespondedAt is { } responded)
                    db.Messages.Add(new TicketMessage { TenantId = northstar.Id, TicketId = ticket.Id, AuthorId = agent.Id, Body = $"Hi {customer.Name.Split(' ')[0]}, thanks for reaching out. I've reviewed your request and I'm looking into this with our team. I'll keep you updated here.", CreatedAt = responded });
                if (i % 4 == 0)
                    db.Messages.Add(new TicketMessage { TenantId = northstar.Id, TicketId = ticket.Id, AuthorId = agent.Id, Body = "Checking the account configuration with the product team. Please keep this context internal.", IsInternal = true, CreatedAt = created.AddMinutes(20) });
                ticket.UpdatedAt = new[] { ticket.UpdatedAt, ticket.FirstRespondedAt ?? created, ticket.ResolvedAt ?? created, i % 4 == 0 ? created.AddMinutes(20) : created }.Max();
                db.AuditEntries.Add(new AuditEntry { TenantId = northstar.Id, Action = "TicketCreated", Description = $"Created ticket #{ticket.Number}: {ticket.Subject}", ActorName = customer.Name, TicketId = ticket.Id, CreatedAt = created });
            }
            var other = new Ticket { TenantId = meridian.Id, Number = 1041, Subject = "Meridian private integration request", Description = "This ticket belongs exclusively to Meridian Labs and must never be accessible to Northstar users.", CustomerId = users[9].Id, ResponseDueAt = now.AddHours(8), ResolutionDueAt = now.AddDays(1) };
            db.Tickets.Add(other);
            db.Messages.Add(new TicketMessage { TenantId = meridian.Id, TicketId = other.Id, AuthorId = users[9].Id, Body = other.Description });
            db.AuditEntries.Add(new AuditEntry { TenantId = northstar.Id, Action = "WorkspaceCreated", Description = "Demonstration workspace initialized with realistic support conversations.", ActorName = "Avenor Demo", CreatedAt = now.AddDays(-9) });
            foreach (var recipient in users.Where(x => x.TenantId == northstar.Id))
                db.Notifications.Add(new Notification { TenantId = northstar.Id, UserId = recipient.Id, Title = "Welcome to your support workspace", Body = "This workspace contains clearly identified demonstration data. All changes are saved to the database.", CreatedAt = now.AddMinutes(-20) });
            await db.SaveChangesAsync(ct);
        }
        finally { db.Seeding = false; }
    }

    private static SupportUser User(Guid tenant, string name, string email, UserRole role, string company, string color) => new() { TenantId = tenant, Name = name, Email = email, Role = role, Company = company, AvatarColor = color };
}
