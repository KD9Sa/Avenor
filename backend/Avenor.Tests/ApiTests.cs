using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Avenor.Application;
using Avenor.Domain;
using Avenor.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Avenor.Tests;

public sealed class ApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    private static async Task RefreshCsrf(HttpClient client)
    {
        var token = (await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf")).GetProperty("token").GetString();
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", token);
    }
    private static async Task<HttpClient> Login(ApiFactory factory, string email = "alex@northstar.demo", string slug = "northstar")
    {
        var client = factory.CreateClient(new()
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        await RefreshCsrf(client);
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            organizationSlug = slug,
            email,
            password = DemoSeeder.Password
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await RefreshCsrf(client);
        return client;
    }
    private static async Task<TicketDto[]> Tickets(HttpClient client) => (await client.GetFromJsonAsync<PageDto<TicketDto>>("/api/tickets?pageSize=100", Json))!.Items.ToArray();

    [Fact]
    public async Task Authentication_requires_csrf_and_uses_httponly_session_cookie()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        var rejected = await client.PostAsJsonAsync("/api/auth/login", new
        {
            organizationSlug = "northstar",
            email = "alex@northstar.demo",
            password = DemoSeeder.Password
        });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        await RefreshCsrf(client);
        var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            organizationSlug = "northstar",
            email = "alex@northstar.demo",
            password = DemoSeeder.Password
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains(login.Headers.GetValues("Set-Cookie"), x => x.StartsWith("avenor.session=") && x.Contains("httponly", StringComparison.OrdinalIgnoreCase) && x.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("password", await login.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Tenants_cannot_read_modify_assign_or_reply_to_each_others_tickets()
    {
        using var factory = new ApiFactory();
        using var northstar = await Login(factory);
        using var meridian = await Login(factory, "admin@meridian.demo", "meridian");
        var ns = await Tickets(northstar);
        var ms = await Tickets(meridian);
        Assert.Equal(46, ns.Length);
        Assert.Single(ms);
        Assert.Empty(ns.Select(t => t.Id).Intersect(ms.Select(t => t.Id)));
        var other = ms[0];
        Assert.Equal(HttpStatusCode.NotFound, (await northstar.GetAsync($"/api/tickets/{other.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await northstar.PatchAsJsonAsync($"/api/tickets/{other.Id}", new
        {
            version = other.Version,
            priority = "High"
        })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await northstar.PostAsJsonAsync($"/api/tickets/{other.Id}/messages", new
        {
            version = other.Version,
            body = "Trying another tenant ticket",
            isInternal = false
        })).StatusCode);
        var foreignUsers = await meridian.GetFromJsonAsync<UserDto[]>("/api/users", Json);
        Assert.Equal(HttpStatusCode.BadRequest, (await northstar.PatchAsJsonAsync($"/api/tickets/{ns[0].Id}", new
        {
            version = ns[0].Version,
            assigneeId = foreignUsers!.First(x => x.Role == "Admin").Id
        })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await northstar.PostAsJsonAsync("/api/tickets", new
        {
            subject = "Foreign customer test",
            description = "Trying to create a ticket for another tenant's customer.",
            priority = "Normal",
            category = "General",
            customerId = other.CustomerId
        })).StatusCode);
    }

    [Fact]
    public async Task Customers_see_only_own_public_conversations_and_have_no_staff_permissions()
    {
        using var factory = new ApiFactory();
        using var admin = await Login(factory);
        using var customer = await Login(factory, "morgan@acme.demo");
        var me = await customer.GetFromJsonAsync<UserDto>("/api/auth/me", Json);
        var own = await Tickets(customer);
        Assert.NotEmpty(own);
        Assert.All(own, x => Assert.Equal(me!.Id, x.CustomerId));
        var other = (await Tickets(admin)).First(x => x.CustomerId != me!.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await customer.GetAsync($"/api/tickets/{other.Id}")).StatusCode);
        var mine = own.First();
        var detail = await customer.GetFromJsonAsync<TicketDetail>($"/api/tickets/{mine.Id}", Json);
        Assert.All(detail!.Messages, m => Assert.False(m.IsInternal));
        Assert.DoesNotContain(detail.History, h => h.Action == "InternalNote");
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PostAsJsonAsync($"/api/tickets/{mine.Id}/messages", new
        {
            version = mine.Version,
            body = "An internal note attempt",
            isInternal = true
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PatchAsJsonAsync($"/api/tickets/{mine.Id}", new
        {
            version = mine.Version,
            priority = "Urgent"
        })).StatusCode);
        foreach (var endpoint in new[] { "/api/users", "/api/customers", "/api/dashboard", "/api/audit", "/api/reports/export" })
            Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync(endpoint)).StatusCode);
    }

    [Fact]
    public async Task Ticket_lifecycle_persists_replies_assignments_concurrency_and_reopening()
    {
        using var factory = new ApiFactory();
        using var admin = await Login(factory);
        using var customer = await Login(factory, "morgan@acme.demo");
        var created = await customer.PostAsJsonAsync("/api/tickets", new
        {
            subject = "Integration lifecycle request",
            description = "This request exercises the complete lifecycle against a real relational database.",
            priority = "High",
            category = "Technical"
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var t = (await created.Content.ReadFromJsonAsync<TicketDto>(Json))!;
        Assert.NotNull(t.AssigneeId);
        Assert.Equal(120, (t.ResponseDueAt - t.CreatedAt).TotalMinutes);
        Assert.Equal(480, (t.ResolutionDueAt - t.CreatedAt).TotalMinutes);
        var users = await admin.GetFromJsonAsync<UserDto[]>("/api/users", Json);
        var nextAgent = users!.First(x => x.Role == "Agent" && x.Id != t.AssigneeId);
        var assign = await admin.PatchAsJsonAsync($"/api/tickets/{t.Id}", new
        {
            version = t.Version,
            assigneeId = nextAgent.Id,
            status = "InProgress"
        });
        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);
        var assigned = (await assign.Content.ReadFromJsonAsync<TicketDto>(Json))!;
        Assert.Equal(nextAgent.Id, assigned.AssigneeId);
        Assert.Equal(nextAgent.Name, assigned.AssigneeName);
        var persisted = await admin.GetFromJsonAsync<TicketDetail>($"/api/tickets/{t.Id}", Json);
        Assert.Equal(nextAgent.Id, persisted!.Ticket.AssigneeId);
        var same = await admin.PatchAsJsonAsync($"/api/tickets/{t.Id}", new
        {
            version = assigned.Version,
            assigneeId = nextAgent.Id
        });
        Assert.Equal(nextAgent.Name, (await same.Content.ReadFromJsonAsync<TicketDto>(Json))!.AssigneeName);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PatchAsJsonAsync($"/api/tickets/{t.Id}", new
        {
            version = t.Version,
            priority = "Low"
        })).StatusCode);
        var note = await admin.PostAsJsonAsync($"/api/tickets/{t.Id}/messages", new
        {
            version = assigned.Version,
            body = "Private troubleshooting context",
            isInternal = true
        });
        Assert.Equal(HttpStatusCode.OK, note.StatusCode);
        var noted = (await note.Content.ReadFromJsonAsync<TicketDetail>(Json))!;
        Assert.Null(noted.Ticket.FirstRespondedAt);
        var reply = await admin.PostAsJsonAsync($"/api/tickets/{t.Id}/messages", new
        {
            version = noted.Ticket.Version,
            body = "We have fixed the integration configuration.",
            isInternal = false
        });
        var replied = (await reply.Content.ReadFromJsonAsync<TicketDetail>(Json))!;
        Assert.NotNull(replied.Ticket.FirstRespondedAt);
        var resolve = await admin.PatchAsJsonAsync($"/api/tickets/{t.Id}", new
        {
            version = replied.Ticket.Version,
            status = "Resolved"
        });
        var resolved = (await resolve.Content.ReadFromJsonAsync<TicketDto>(Json))!;
        Assert.NotNull(resolved.ResolvedAt);
        var reopen = await customer.PostAsJsonAsync($"/api/tickets/{t.Id}/messages", new
        {
            version = resolved.Version,
            body = "Thanks, I still need help with one additional detail.",
            isInternal = false
        });
        var reopened = (await reopen.Content.ReadFromJsonAsync<TicketDetail>(Json))!;
        Assert.Equal(TicketStatus.Open, reopened.Ticket.Status);
        Assert.Null(reopened.Ticket.ResolvedAt);
        Assert.DoesNotContain(reopened.Messages, x => x.IsInternal);
        Assert.DoesNotContain(reopened.History, x => x.Action == "InternalNote");
        Assert.Equal(HttpStatusCode.BadRequest, (await customer.PostAsJsonAsync("/api/tickets", new
        {
            subject = " ",
            description = " ",
            priority = "Nope",
            category = "General"
        })).StatusCode);
    }

    [Fact]
    public async Task Last_admin_is_protected_and_agent_cannot_change_settings()
    {
        using var factory = new ApiFactory();
        using var admin = await Login(factory);
        using var agent = await Login(factory, "jordan@northstar.demo");
        var me = (await admin.GetFromJsonAsync<UserDto>("/api/auth/me", Json))!;
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PatchAsJsonAsync($"/api/users/{me.Id}", new
        {
            role = "Agent"
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PatchAsJsonAsync($"/api/users/{me.Id}", new
        {
            isActive = false
        })).StatusCode);
        var settings = await admin.GetFromJsonAsync<SettingsDto>("/api/settings", Json);
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.PutAsJsonAsync("/api/settings", settings)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/settings", settings)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/settings", settings! with
        {
            SlaPolicies = [new("Urgent", -1, 3)]
        })).StatusCode);
    }

    [Fact]
    public async Task Deactivated_users_lose_existing_sessions_and_notifications_are_per_user()
    {
        using var factory = new ApiFactory();
        using var admin = await Login(factory);
        using var customer = await Login(factory, "morgan@acme.demo");
        var me = (await customer.GetFromJsonAsync<UserDto>("/api/auth/me", Json))!;
        var adminNotes = await admin.GetFromJsonAsync<JsonElement[]>("/api/notifications");
        var customerNotes = await customer.GetFromJsonAsync<JsonElement[]>("/api/notifications");
        await admin.PostAsJsonAsync("/api/notifications/read", new
        {
            ids = customerNotes!.Select(x => x.GetProperty("id").GetString())
        });
        var unchanged = await customer.GetFromJsonAsync<JsonElement[]>("/api/notifications");
        Assert.False(unchanged![0].GetProperty("isRead").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/users/{me.Id}", new
        {
            isActive = false
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await customer.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Search_pagination_reports_and_audit_use_persisted_tenant_data()
    {
        using var factory = new ApiFactory();
        using var admin = await Login(factory);
        var results = (await admin.GetFromJsonAsync<PageDto<TicketDto>>("/api/tickets?search=billing&pageSize=2&sort=number&direction=asc", Json))!;
        Assert.True(results.Total > 2);
        Assert.Equal(2, results.Items.Count);
        Assert.True(results.Items[0].Number < results.Items[1].Number);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/tickets?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/tickets?sort=sql")).StatusCode);
        var dashboard = await admin.GetFromJsonAsync<JsonElement>("/api/dashboard?days=7");
        Assert.Equal(7, dashboard.GetProperty("volume").GetArrayLength());
        Assert.True(dashboard.GetProperty("totalTickets").GetInt32() > 0);
        var csv = await admin.GetAsync("/api/reports/export?days=30");
        Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
        Assert.Contains("Subject", await csv.Content.ReadAsStringAsync());
        var audit = await admin.GetFromJsonAsync<PageDto<AuditDto>>("/api/audit", Json);
        Assert.True(audit!.Total >= 47);
    }

    [Fact]
    public async Task Database_rejects_tenant_write_bypass_and_cross_tenant_foreign_keys()
    {
        using var factory = new ApiFactory();
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AvenorDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<Avenor.Api.HttpTenantContext>();
        var orgs = await db.Organizations.IgnoreQueryFilters().ToListAsync();
        tenant.SetSystemTenant(orgs[0].Id);
        db.AuditEntries.Add(new AuditEntry { TenantId = orgs[1].Id, Action = "Bypass", Description = "Must fail", ActorName = "Test" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        var otherCustomer = await db.Users.IgnoreQueryFilters().FirstAsync(x => x.TenantId == orgs[1].Id && x.Role == UserRole.Customer);
        db.Tickets.Add(new Ticket { TenantId = orgs[0].Id, Number = 9999, Subject = "Invalid foreign key test", Description = "Constraint test", CustomerId = otherCustomer.Id, ResponseDueAt = DateTime.UtcNow, ResolutionDueAt = DateTime.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Offline_bootstrap_creates_hashed_admin_and_does_not_reset_existing_credentials()
    {
        using var factory = new ApiFactory();
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AvenorDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.IPasswordHasher<SupportUser>>();
        const string password = "UniqueBootstrap!2026";
        await WorkspaceBootstrapper.CreateAsync(db, "Production Test", "production-test", "Test Admin", "admin@production.test", password, hasher.HashPassword);
        var org = await db.Organizations.IgnoreQueryFilters().SingleAsync(x => x.Slug == "production-test");
        var admin = await db.Users.IgnoreQueryFilters().SingleAsync(x => x.TenantId == org.Id);
        Assert.Equal(UserRole.Admin, admin.Role);
        Assert.NotEqual(password, admin.PasswordHash);
        Assert.Equal(Microsoft.AspNetCore.Identity.PasswordVerificationResult.Success, hasher.VerifyHashedPassword(admin, admin.PasswordHash, password));
        Assert.Equal(4, await db.SlaPolicies.IgnoreQueryFilters().CountAsync(x => x.TenantId == org.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => WorkspaceBootstrapper.CreateAsync(db, "Production Test", "production-test", "Test Admin", "admin@production.test", "AnotherBootstrap!2026", hasher.HashPassword));
    }

    [Fact]
    public async Task Malformed_shapes_and_overflowing_pagination_return_validation_errors()
    {
        using var factory = new ApiFactory();
        using var admin = await Login(factory);
        var t = (await Tickets(admin))[0];
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/api/tickets/{t.Id}", new
        {
            version = "1",
            status = "Open"
        })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/tickets?page=2147483647&pageSize=100")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/audit?page=2147483647&pageSize=100")).StatusCode);
        var settings = (await admin.GetFromJsonAsync<SettingsDto>("/api/settings", Json))!;
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/settings", settings with
        {
            SlaPolicies = [null!, null!, null!, null!]
        })).StatusCode);
        var found = (await admin.GetFromJsonAsync<PageDto<TicketDto>>($"/api/tickets?search=AV-{t.Number}", Json))!;
        Assert.Single(found.Items);
        Assert.Equal(t.Id, found.Items[0].Id);
    }

    [Fact]
    public void Realtime_invalidations_never_cross_tenants()
    {
        var events = new Avenor.Api.LiveEvents();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var first = events.Subscribe(a);
        var second = events.Subscribe(b);
        events.Publish(a);
        Assert.True(first.Reader.TryRead(out _));
        Assert.False(second.Reader.TryRead(out _));
        events.Unsubscribe(first.Id);
        events.Unsubscribe(second.Id);
    }
}
