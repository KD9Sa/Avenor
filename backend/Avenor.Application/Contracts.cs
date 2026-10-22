using System.ComponentModel.DataAnnotations;
using Avenor.Domain;

namespace Avenor.Application;

public interface ITenantContext
{
    Guid? TenantId
    {
        get;
    }
    Guid? UserId
    {
        get;
    }
    string Role
    {
        get;
    }
    string UserName
    {
        get;
    }
    bool IsCustomer
    {
        get;
    }
}

public record UserDto(Guid Id, string Name, string Email, string Role, Guid TenantId, string OrganizationName, string OrganizationSlug, bool IsActive, string AvatarColor);
public record TicketDto(Guid Id, int Number, string Subject, string Description, TicketStatus Status, Priority Priority, string Category, TicketChannel Channel, Guid CustomerId, string CustomerName, string CustomerEmail, Guid? AssigneeId, string? AssigneeName, DateTime CreatedAt, DateTime UpdatedAt, DateTime ResponseDueAt, DateTime ResolutionDueAt, DateTime? FirstRespondedAt, DateTime? ResolvedAt, bool IsEscalated, int Version, string[] Tags);
public record MessageDto(Guid Id, string Body, bool IsInternal, Guid AuthorId, string AuthorName, string AuthorRole, DateTime CreatedAt);
public record AuditDto(Guid Id, string Action, string Description, string ActorName, DateTime CreatedAt, Guid? TicketId);
public record TicketDetail(TicketDto Ticket, IReadOnlyList<MessageDto> Messages, IReadOnlyList<AuditDto> History);
public record PageDto<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
public record LoginRequest([Required, StringLength(64)] string OrganizationSlug, [Required, EmailAddress, StringLength(254)] string Email, [Required, StringLength(128)] string Password);
public record CreateUserRequest([Required, StringLength(80, MinimumLength = 2)] string Name, [Required, EmailAddress, StringLength(254)] string Email, [Required] string Role, [Required, StringLength(128, MinimumLength = 12)] string Password);
public record UpdateUserRequest([StringLength(80, MinimumLength = 2)] string? Name, string? Role, bool? IsActive);
public record CreateTicketRequest([Required, StringLength(180, MinimumLength = 5)] string Subject, [Required, StringLength(10000, MinimumLength = 10)] string Description, [Required] string Priority, [Required, StringLength(40)] string Category, Guid? CustomerId, string? Channel, string[]? Tags);
public record AddMessageRequest([Required, StringLength(10000, MinimumLength = 1)] string Body, bool IsInternal, int Version);
public record SlaPolicyDto(string Priority, int ResponseMinutes, int ResolutionMinutes);
public record SettingsDto(string OrganizationName, string OrganizationSlug, string AssignmentStrategy, SlaPolicyDto[] SlaPolicies, string BusinessHours);
public record MarkReadRequest(Guid[] Ids);

public static class DtoValidation
{
    public static Dictionary<string, string[]> Validate(object request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, true);
        return results.SelectMany(result => result.MemberNames.DefaultIfEmpty("request").Select(member => (member, message: result.ErrorMessage ?? "Invalid value")))
            .GroupBy(x => x.member).ToDictionary(x => char.ToLowerInvariant(x.Key[0]) + x.Key[1..], x => x.Select(r => r.message).ToArray());
    }
}
