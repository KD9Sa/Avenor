using System.Text.Json;
using Avenor.Application;
using Avenor.Domain;

namespace Avenor.Infrastructure;

public static class DtoMapper
{
    public static TicketDto ToDto(this Ticket t) => new(t.Id, t.Number, t.Subject, t.Description, t.Status, t.Priority, t.Category, t.Channel, t.CustomerId, t.Customer.Name, t.Customer.Email, t.AssigneeId, t.Assignee?.Name, t.CreatedAt, t.UpdatedAt, t.ResponseDueAt, t.ResolutionDueAt, t.FirstRespondedAt, t.ResolvedAt, t.IsEscalated, t.Version, JsonSerializer.Deserialize<string[]>(t.TagsJson) ?? []);
    public static MessageDto ToDto(this TicketMessage m) => new(m.Id, m.Body, m.IsInternal, m.AuthorId, m.Author.Name, m.Author.Role.ToString(), m.CreatedAt);
    public static AuditDto ToDto(this AuditEntry a) => new(a.Id, a.Action, a.Description, a.ActorName, a.CreatedAt, a.TicketId);
    public static UserDto ToDto(this SupportUser u, Organization o) => new(u.Id, u.Name, u.Email, u.Role.ToString(), u.TenantId, o.Name, o.Slug, u.IsActive, u.AvatarColor);
}
