using Avenor.Domain;
using Avenor.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Avenor.Api;

public sealed class EscalationWorker(IServiceScopeFactory scopes, LiveEvents events, ILogger<EscalationWorker> logger, IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (configuration.GetValue<bool>("DisableBackgroundWorkers"))
            return;
        // Delay until migrations/seeding and host startup have completed.
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                    return;
                using var discovery = scopes.CreateScope();
                var discoveryDb = discovery.ServiceProvider.GetRequiredService<AvenorDbContext>();
                var tenants = await discoveryDb.Organizations.IgnoreQueryFilters().AsNoTracking().Select(x => x.Id).ToListAsync(stoppingToken);
                foreach (var tenant in tenants)
                {
                    using var scope = scopes.CreateScope();
                    scope.ServiceProvider.GetRequiredService<HttpTenantContext>().SetSystemTenant(tenant);
                    var db = scope.ServiceProvider.GetRequiredService<AvenorDbContext>();
                    var now = DateTime.UtcNow;
                    var overdue = await db.Tickets.Where(x => !x.IsEscalated && x.Status != TicketStatus.Resolved && x.Status != TicketStatus.Closed && ((x.FirstRespondedAt == null && x.ResponseDueAt < now) || x.ResolutionDueAt < now)).ToListAsync(stoppingToken);
                    if (overdue.Count == 0)
                        continue;
                    var staff = await db.Users.Where(x => x.IsActive && x.Role != UserRole.Customer).Select(x => x.Id).ToListAsync(stoppingToken);
                    foreach (var ticket in overdue)
                    {
                        ticket.IsEscalated = true;
                        ticket.Version++;
                        db.AuditEntries.Add(new AuditEntry { TenantId = tenant, TicketId = ticket.Id, Action = "SlaEscalated", Description = $"Ticket #{ticket.Number} breached its SLA and was escalated.", ActorName = "SLA monitor" });
                        foreach (var recipient in staff)
                            db.Notifications.Add(new Notification { TenantId = tenant, UserId = recipient, TicketId = ticket.Id, Title = $"SLA breached · #{ticket.Number}", Body = ticket.Subject });
                    }
                    await db.SaveChangesAsync(stoppingToken);
                    events.Publish(tenant);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (DbUpdateConcurrencyException) { logger.LogInformation("SLA scan deferred because a ticket was updated concurrently."); }
            catch (Exception ex) { logger.LogError(ex, "SLA escalation scan failed; will retry at the next interval."); }
        }
    }
}
