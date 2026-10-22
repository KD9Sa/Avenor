namespace Avenor.Domain;

public sealed class RuleViolation(string message) : Exception(message);

public static class TicketRules
{
    public static bool IsActive(TicketStatus status) => status is not (TicketStatus.Resolved or TicketStatus.Closed);

    public static void Transition(Ticket ticket, TicketStatus next, DateTime now)
    {
        if (ticket.Status == next)
            return;
        var allowed = ticket.Status switch
        {
            TicketStatus.Open => next is TicketStatus.InProgress or TicketStatus.Pending or TicketStatus.Resolved,
            TicketStatus.InProgress => next is TicketStatus.Open or TicketStatus.Pending or TicketStatus.Resolved,
            TicketStatus.Pending => next is TicketStatus.Open or TicketStatus.InProgress or TicketStatus.Resolved,
            TicketStatus.Resolved => next is TicketStatus.Open or TicketStatus.Closed,
            _ => false
        };
        if (!allowed)
            throw new RuleViolation($"Cannot change {ticket.Status} to {next}.");
        ticket.Status = next;
        ticket.ResolvedAt = next is TicketStatus.Resolved or TicketStatus.Closed ? ticket.ResolvedAt ?? now : null;
    }

    public static void AddReply(Ticket ticket, UserRole authorRole, bool isInternal, DateTime now)
    {
        if (ticket.Status == TicketStatus.Closed)
            throw new RuleViolation("Closed tickets cannot receive replies. Create a new ticket instead.");
        if (authorRole == UserRole.Customer && isInternal)
            throw new RuleViolation("Customers cannot create internal notes.");
        if (!isInternal && authorRole != UserRole.Customer)
            ticket.FirstRespondedAt ??= now;
        if (!isInternal && authorRole == UserRole.Customer && ticket.Status == TicketStatus.Resolved)
            Transition(ticket, TicketStatus.Open, now);
    }

    public static bool IsOverdue(Ticket ticket, DateTime now) => IsActive(ticket.Status) &&
        ((ticket.FirstRespondedAt is null && ticket.ResponseDueAt < now) || ticket.ResolutionDueAt < now);

    public static Guid? Assign(AssignmentStrategy strategy, IReadOnlyList<(Guid Id, int Active)> agents, int cursor)
    {
        if (strategy == AssignmentStrategy.Manual || agents.Count == 0)
            return null;
        return strategy == AssignmentStrategy.LeastBusy
            ? agents.OrderBy(x => x.Active).ThenBy(x => x.Id).First().Id
            : agents.OrderBy(x => x.Id).ElementAt(Math.Abs(cursor % agents.Count)).Id;
    }
}
