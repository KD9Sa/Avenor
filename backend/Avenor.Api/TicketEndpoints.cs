using System.Text.Json;
using Avenor.Application;
using Avenor.Domain;
using Avenor.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Avenor.Api;

public static class TicketEndpoints
{
    internal static IQueryable<Ticket> VisibleTickets(AvenorDbContext db, ITenantContext tenant) => tenant.IsCustomer ? db.Tickets.Where(x => x.CustomerId == tenant.UserId) : db.Tickets;
    internal static IQueryable<Ticket> WithPeople(IQueryable<Ticket> query) => query.Include(x => x.Customer).Include(x => x.Assignee);
    internal static bool TryEnum<T>(string? value, out T parsed) where T : struct, Enum => Enum.TryParse(value, out parsed) && Enum.GetNames<T>().Contains(value);
    private static bool TagsValid(string[]? tags) => tags is null || (tags.Length <= 8 && tags.All(x => !string.IsNullOrWhiteSpace(x) && x.Trim().Length <= 30));
    internal static AuditEntry Audit(ITenantContext tenant, string action, string description, Guid? ticketId = null) => new() { TenantId = tenant.TenantId!.Value, ActorName = tenant.UserName, Action = action, Description = description, TicketId = ticketId };

    private static async Task<TicketDetail> Detail(AvenorDbContext db, Ticket ticket, ITenantContext tenant)
    {
        var messages = await db.Messages.AsNoTracking().Include(x => x.Author).Where(x => x.TicketId == ticket.Id && (!tenant.IsCustomer || !x.IsInternal)).OrderBy(x => x.CreatedAt).ToListAsync();
        var historyQuery = db.AuditEntries.AsNoTracking().Where(x => x.TicketId == ticket.Id);
        if (tenant.IsCustomer)
            historyQuery = historyQuery.Where(x => x.Action == "TicketCreated" || x.Action == "StatusChanged" || x.Action == "PublicReply");
        var history = await historyQuery.OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync();
        return new TicketDetail(ticket.ToDto(), messages.Select(x => x.ToDto()).ToArray(), history.Select(x => x.ToDto()).ToArray());
    }

    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/tickets").RequireAuthorization();
        group.MapGet("", async (HttpRequest request, AvenorDbContext db, ITenantContext tenant) =>
        {
            var q = VisibleTickets(db, tenant).AsNoTracking();
            var search = request.Query["search"].ToString().Trim();
            if (search.Length > 180)
                return Results.Problem(statusCode: 400, title: "Search is too long");
            if (search.Length > 0)
            {
                var term = search.ToLowerInvariant();
                var identifier = search.TrimStart('#');
                if (identifier.StartsWith("AV-", StringComparison.OrdinalIgnoreCase))
                    identifier = identifier[3..];
                var number = int.TryParse(identifier, out var n) ? n : -1;
                q = q.Where(x => x.Subject.ToLower().Contains(term) || x.Customer.Name.ToLower().Contains(term) || x.Customer.Email.ToLower().Contains(term) || x.Number == number);
            }
            if (!string.IsNullOrEmpty(request.Query["status"]))
            {
                if (!TryEnum<TicketStatus>(request.Query["status"], out var status))
                    return Results.Problem(statusCode: 400, title: "Invalid ticket status");
                q = q.Where(x => x.Status == status);
            }
            if (!string.IsNullOrEmpty(request.Query["priority"]))
            {
                if (!TryEnum<Priority>(request.Query["priority"], out var priority))
                    return Results.Problem(statusCode: 400, title: "Invalid ticket priority");
                q = q.Where(x => x.Priority == priority);
            }
            var assignee = request.Query["assigneeId"].ToString();
            if (assignee == "unassigned")
                q = q.Where(x => x.AssigneeId == null);
            else if (assignee == "me")
                q = q.Where(x => x.AssigneeId == tenant.UserId);
            else if (!string.IsNullOrEmpty(assignee))
            {
                if (!Guid.TryParse(assignee, out var id))
                    return Results.Problem(statusCode: 400, title: "Invalid assignee");
                q = q.Where(x => x.AssigneeId == id);
            }
            if (!int.TryParse(request.Query["page"].FirstOrDefault() ?? "1", out var page) || page < 1 || !int.TryParse(request.Query["pageSize"].FirstOrDefault() ?? "10", out var pageSize) || pageSize is < 1 or > 100)
                return Results.Problem(statusCode: 400, title: "Invalid pagination", detail: "Page starts at 1; pageSize must be between 1 and 100.");
            var sort = request.Query["sort"].FirstOrDefault() ?? "updatedAt";
            var direction = request.Query["direction"].FirstOrDefault() ?? "desc";
            if (direction is not ("asc" or "desc"))
                return Results.Problem(statusCode: 400, title: "Invalid sort direction");
            var desc = direction == "desc";
            q = sort switch
            {
                "updatedAt" => desc ? q.OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.Id) : q.OrderBy(x => x.UpdatedAt).ThenBy(x => x.Id),
                "createdAt" => desc ? q.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id) : q.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id),
                "priority" => desc ? q.OrderByDescending(x => x.Priority).ThenBy(x => x.Id) : q.OrderBy(x => x.Priority).ThenBy(x => x.Id),
                "subject" => desc ? q.OrderByDescending(x => x.Subject).ThenBy(x => x.Id) : q.OrderBy(x => x.Subject).ThenBy(x => x.Id),
                "number" => desc ? q.OrderByDescending(x => x.Number).ThenBy(x => x.Id) : q.OrderBy(x => x.Number).ThenBy(x => x.Id),
                _ => null!
            };
            if (q is null)
                return Results.Problem(statusCode: 400, title: "Invalid sort column");
            if ((long)(page - 1) * pageSize > int.MaxValue)
                return Results.Problem(statusCode: 400, title: "Page is out of range");
            var total = await q.CountAsync();
            var tickets = await WithPeople(q).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return Results.Ok(new PageDto<TicketDto>(tickets.Select(x => x.ToDto()).ToArray(), total, page, pageSize));
        });
        group.MapGet("/{id:guid}", async (Guid id, AvenorDbContext db, ITenantContext tenant) =>
        {
            var ticket = await WithPeople(VisibleTickets(db, tenant)).AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
            return ticket is null ? Results.NotFound() : Results.Ok(await Detail(db, ticket, tenant));
        });
        group.MapPost("", async (CreateTicketRequest request, AvenorDbContext db, ITenantContext tenant, LiveEvents events) =>
        {
            var errors = DtoValidation.Validate(request);
            if (errors.Count > 0)
                return Results.ValidationProblem(errors);
            if (request.Subject.Trim().Length < 5 || request.Description.Trim().Length < 10 || string.IsNullOrWhiteSpace(request.Category))
                return Results.Problem(statusCode: 400, title: "A subject, description and category are required");
            if (!TryEnum<Priority>(request.Priority, out var priority) || !TryEnum<TicketChannel>(request.Channel ?? "Portal", out var channel))
                return Results.Problem(statusCode: 400, title: "Invalid priority or channel");
            if (!TagsValid(request.Tags))
                return Results.Problem(statusCode: 400, title: "Use up to 8 tags of at most 30 characters");
            var customerId = tenant.IsCustomer ? tenant.UserId : request.CustomerId;
            var customer = await db.Users.SingleOrDefaultAsync(x => x.Id == customerId && x.Role == UserRole.Customer && x.IsActive);
            if (customer is null)
                return Results.Problem(statusCode: 400, title: "Select an active customer in this workspace");
            var org = await db.Organizations.SingleAsync();
            var policy = await db.SlaPolicies.SingleAsync(x => x.Priority == priority);
            var staff = await db.Users.Where(x => x.IsActive && x.Role != UserRole.Customer).OrderBy(x => x.Id).Select(x => x.Id).ToListAsync();
            var counts = await db.Tickets.Where(x => x.AssigneeId != null && x.Status != TicketStatus.Resolved && x.Status != TicketStatus.Closed).GroupBy(x => x.AssigneeId).Select(x => new { Id = x.Key, Count = x.Count() }).ToListAsync();
            var assigneeId = TicketRules.Assign(org.AssignmentStrategy, staff.Select(x => (x, counts.FirstOrDefault(c => c.Id == x)?.Count ?? 0)).ToArray(), org.AssignmentCursor);
            var now = DateTime.UtcNow;
            var ticket = new Ticket { TenantId = tenant.TenantId!.Value, Number = org.NextTicketNumber++, Subject = request.Subject.Trim(), Description = request.Description.Trim(), Priority = priority, Channel = channel, Category = request.Category.Trim(), CustomerId = customer.Id, Customer = customer, AssigneeId = assigneeId, CreatedAt = now, UpdatedAt = now, ResponseDueAt = now.AddMinutes(policy.ResponseMinutes), ResolutionDueAt = now.AddMinutes(policy.ResolutionMinutes), TagsJson = JsonSerializer.Serialize(request.Tags?.Select(x => x.Trim()).Distinct().ToArray() ?? []) };
            org.AssignmentCursor++;
            org.Version++;
            db.Tickets.Add(ticket);
            db.Messages.Add(new TicketMessage { TenantId = ticket.TenantId, TicketId = ticket.Id, AuthorId = tenant.UserId!.Value, Body = ticket.Description, CreatedAt = now });
            db.AuditEntries.Add(Audit(tenant, "TicketCreated", $"Created ticket #{ticket.Number}: {ticket.Subject}", ticket.Id));
            if (assigneeId is { } assigned)
                db.Notifications.Add(new Notification { TenantId = ticket.TenantId, UserId = assigned, Title = $"Ticket #{ticket.Number} assigned to you", Body = ticket.Subject, TicketId = ticket.Id });
            await db.SaveChangesAsync();
            if (ticket.AssigneeId is not null)
                await db.Entry(ticket).Reference(x => x.Assignee).LoadAsync();
            events.Publish(ticket.TenantId);
            return Results.Created($"/api/tickets/{ticket.Id}", ticket.ToDto());
        });
        group.MapPatch("/{id:guid}", async (Guid id, JsonElement request, AvenorDbContext db, ITenantContext tenant, LiveEvents events) =>
        {
            if (request.ValueKind != JsonValueKind.Object || !request.TryGetProperty("version", out var v) || v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var version))
                return Results.Problem(statusCode: 400, title: "A numeric ticket version is required");
            var ticket = await WithPeople(db.Tickets).SingleOrDefaultAsync(x => x.Id == id);
            if (ticket is null)
                return Results.NotFound();
            if (ticket.Version != version)
                return Results.Problem(statusCode: 409, title: "This ticket has changed", detail: "Refresh the ticket before saving.");
            var changes = new List<string>();
            if (request.TryGetProperty("status", out var s))
            {
                if (s.ValueKind != JsonValueKind.String || !TryEnum<TicketStatus>(s.GetString(), out var status))
                    return Results.Problem(statusCode: 400, title: "Invalid status");
                if (status != ticket.Status)
                {
                    changes.Add($"status from {ticket.Status} to {status}");
                    TicketRules.Transition(ticket, status, DateTime.UtcNow);
                }
            }
            if (request.TryGetProperty("priority", out var p))
            {
                if (p.ValueKind != JsonValueKind.String || !TryEnum<Priority>(p.GetString(), out var priority))
                    return Results.Problem(statusCode: 400, title: "Invalid priority");
                if (priority != ticket.Priority)
                    changes.Add($"priority to {priority}");
                ticket.Priority = priority;
            }
            if (request.TryGetProperty("assigneeId", out var a))
            {
                Guid? aid = null;
                if (a.ValueKind != JsonValueKind.Null)
                {
                    if (a.ValueKind != JsonValueKind.String || !Guid.TryParse(a.GetString(), out var parsed))
                        return Results.Problem(statusCode: 400, title: "Invalid assignee");
                    aid = parsed;
                    if (!await db.Users.AnyAsync(x => x.Id == aid && x.IsActive && x.Role != UserRole.Customer))
                        return Results.Problem(statusCode: 400, title: "Assignee must be an active staff member in this workspace");
                }
                if (aid != ticket.AssigneeId)
                    changes.Add(aid is null ? "assignee to unassigned" : "assignee");
                var selectedAssignee = aid is null ? null : await db.Users.SingleAsync(x => x.Id == aid);
                ticket.Assignee = selectedAssignee;
                ticket.AssigneeId = aid;
                if (aid is { } recipient && changes.Any(x => x.StartsWith("assignee")))
                    db.Notifications.Add(new Notification { TenantId = ticket.TenantId, UserId = recipient, Title = $"Ticket #{ticket.Number} assigned to you", Body = ticket.Subject, TicketId = ticket.Id });
            }
            if (request.TryGetProperty("tags", out var tags))
            {
                if (tags.ValueKind != JsonValueKind.Array || tags.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String))
                    return Results.Problem(statusCode: 400, title: "Invalid tags");
                var values = tags.EnumerateArray().Select(x => x.GetString()!).ToArray();
                if (!TagsValid(values))
                    return Results.Problem(statusCode: 400, title: "Use up to 8 tags of at most 30 characters");
                ticket.TagsJson = JsonSerializer.Serialize(values.Select(x => x.Trim()).Distinct().ToArray());
                changes.Add("tags");
            }
            if (changes.Count == 0)
                return Results.Ok(ticket.ToDto());
            ticket.Version++;
            ticket.UpdatedAt = DateTime.UtcNow;
            db.AuditEntries.Add(Audit(tenant, request.TryGetProperty("status", out _) ? "StatusChanged" : "TicketUpdated", $"Updated {string.Join(", ", changes)} on #{ticket.Number}.", ticket.Id));
            await db.SaveChangesAsync();
            if (ticket.AssigneeId is not null)
                await db.Entry(ticket).Reference(x => x.Assignee).LoadAsync();
            events.Publish(ticket.TenantId);
            return Results.Ok(ticket.ToDto());
        }).RequireAuthorization("Staff");
        group.MapPost("/{id:guid}/messages", async (Guid id, AddMessageRequest request, AvenorDbContext db, ITenantContext tenant, LiveEvents events) =>
        {
            var errors = DtoValidation.Validate(request);
            if (errors.Count > 0)
                return Results.ValidationProblem(errors);
            if (string.IsNullOrWhiteSpace(request.Body))
                return Results.Problem(statusCode: 400, title: "Write a message before sending");
            if (request.IsInternal && tenant.IsCustomer)
                return Results.Forbid();
            var ticket = await WithPeople(VisibleTickets(db, tenant)).SingleOrDefaultAsync(x => x.Id == id);
            if (ticket is null)
                return Results.NotFound();
            if (ticket.Version != request.Version)
                return Results.Problem(statusCode: 409, title: "This ticket has changed", detail: "Refresh the conversation before replying.");
            var now = DateTime.UtcNow;
            TicketRules.AddReply(ticket, Enum.Parse<UserRole>(tenant.Role), request.IsInternal, now);
            ticket.Version++;
            ticket.UpdatedAt = now;
            db.Messages.Add(new TicketMessage { TenantId = ticket.TenantId, TicketId = id, AuthorId = tenant.UserId!.Value, Body = request.Body.Trim(), IsInternal = request.IsInternal, CreatedAt = now });
            db.AuditEntries.Add(Audit(tenant, request.IsInternal ? "InternalNote" : "PublicReply", request.IsInternal ? "Added an internal team note." : "Added a public reply.", id));
            var recipients = tenant.IsCustomer ? await db.Users.Where(x => x.IsActive && x.Role != UserRole.Customer).Select(x => x.Id).ToListAsync() : request.IsInternal ? [] : new List<Guid> { ticket.CustomerId };
            foreach (var recipient in recipients.Where(x => x != tenant.UserId))
                db.Notifications.Add(new Notification { TenantId = ticket.TenantId, UserId = recipient, Title = $"New reply on #{ticket.Number}", Body = ticket.Subject, TicketId = ticket.Id });
            await db.SaveChangesAsync();
            events.Publish(ticket.TenantId);
            return Results.Ok(await Detail(db, ticket, tenant));
        });
    }
}
