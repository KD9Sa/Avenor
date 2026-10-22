namespace Avenor.Domain;

public enum UserRole
{
    Admin, Agent, Customer
}
public enum TicketStatus
{
    Open, InProgress, Pending, Resolved, Closed
}
public enum Priority
{
    Low, Normal, High, Urgent
}
public enum TicketChannel
{
    Email, Portal, Chat
}
public enum AssignmentStrategy
{
    Manual, LeastBusy, RoundRobin
}

public abstract class TenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId
    {
        get; set;
    }
}

public sealed class Organization
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public AssignmentStrategy AssignmentStrategy
    {
        get; set;
    }
    public string BusinessHours { get; set; } = "24/7 (elapsed-time SLA)";
    public int NextTicketNumber { get; set; } = 1041;
    public int AssignmentCursor
    {
        get; set;
    }
    public int Version { get; set; } = 1;
}

public sealed class SupportUser : TenantEntity
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public UserRole Role
    {
        get; set;
    }
    public string Company { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public string AvatarColor { get; set; } = "#3659E3";
}

public sealed class Ticket : TenantEntity
{
    public int Number
    {
        get; set;
    }
    public string Subject { get; set; } = "";
    public string Description { get; set; } = "";
    public TicketStatus Status { get; set; } = TicketStatus.Open;
    public Priority Priority { get; set; } = Priority.Normal;
    public string Category { get; set; } = "General";
    public TicketChannel Channel { get; set; } = TicketChannel.Portal;
    public Guid CustomerId
    {
        get; set;
    }
    public SupportUser Customer { get; set; } = null!;
    public Guid? AssigneeId
    {
        get; set;
    }
    public SupportUser? Assignee
    {
        get; set;
    }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ResponseDueAt
    {
        get; set;
    }
    public DateTime ResolutionDueAt
    {
        get; set;
    }
    public DateTime? FirstRespondedAt
    {
        get; set;
    }
    public DateTime? ResolvedAt
    {
        get; set;
    }
    public bool IsEscalated
    {
        get; set;
    }
    public int Version { get; set; } = 1;
    public string TagsJson { get; set; } = "[]";
}

public sealed class TicketMessage : TenantEntity
{
    public Guid TicketId
    {
        get; set;
    }
    public Ticket Ticket { get; set; } = null!;
    public Guid AuthorId
    {
        get; set;
    }
    public SupportUser Author { get; set; } = null!;
    public string Body { get; set; } = "";
    public bool IsInternal
    {
        get; set;
    }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class SlaPolicy : TenantEntity
{
    public Priority Priority
    {
        get; set;
    }
    public int ResponseMinutes
    {
        get; set;
    }
    public int ResolutionMinutes
    {
        get; set;
    }
}

public sealed class AuditEntry : TenantEntity
{
    public string Action { get; set; } = "";
    public string Description { get; set; } = "";
    public string ActorName { get; set; } = "";
    public Guid? TicketId
    {
        get; set;
    }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class Notification : TenantEntity
{
    public Guid? UserId
    {
        get; set;
    }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public Guid? TicketId
    {
        get; set;
    }
    public bool IsRead
    {
        get; set;
    }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
