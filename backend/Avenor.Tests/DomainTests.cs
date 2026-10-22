using Avenor.Domain;
using Xunit;

namespace Avenor.Tests;

public sealed class DomainTests
{
    [Theory]
    [InlineData(TicketStatus.Open, TicketStatus.InProgress)]
    [InlineData(TicketStatus.Open, TicketStatus.Pending)]
    [InlineData(TicketStatus.Open, TicketStatus.Resolved)]
    [InlineData(TicketStatus.InProgress, TicketStatus.Resolved)]
    [InlineData(TicketStatus.Pending, TicketStatus.Open)]
    [InlineData(TicketStatus.Resolved, TicketStatus.Open)]
    [InlineData(TicketStatus.Resolved, TicketStatus.Closed)]
    public void Lifecycle_accepts_documented_transitions(TicketStatus from, TicketStatus to)
    {
        var t = new Ticket { Status = from };
        var now = DateTime.UtcNow;
        TicketRules.Transition(t, to, now);
        Assert.Equal(to, t.Status);
        Assert.Equal(to is TicketStatus.Resolved or TicketStatus.Closed, t.ResolvedAt is not null);
    }
    [Theory]
    [InlineData(TicketStatus.Closed, TicketStatus.Open)]
    [InlineData(TicketStatus.Open, TicketStatus.Closed)]
    [InlineData(TicketStatus.Resolved, TicketStatus.Pending)]
    public void Lifecycle_rejects_invalid_transitions(TicketStatus from, TicketStatus to) => Assert.Throws<RuleViolation>(() => TicketRules.Transition(new Ticket { Status = from }, to, DateTime.UtcNow));

    [Fact]
    public void Customer_reply_reopens_resolution_and_staff_public_reply_records_first_response()
    {
        var now = DateTime.UtcNow;
        var t = new Ticket { Status = TicketStatus.Resolved, ResolvedAt = now.AddHours(-1) };
        TicketRules.AddReply(t, UserRole.Customer, false, now);
        Assert.Equal(TicketStatus.Open, t.Status);
        Assert.Null(t.ResolvedAt);
        Assert.Null(t.FirstRespondedAt);
        TicketRules.AddReply(t, UserRole.Agent, true, now);
        Assert.Null(t.FirstRespondedAt);
        TicketRules.AddReply(t, UserRole.Agent, false, now);
        TicketRules.AddReply(t, UserRole.Agent, false, now.AddMinutes(1));
        Assert.Equal(now, t.FirstRespondedAt);
    }
    [Fact]
    public void Closed_tickets_reject_replies_and_customers_cannot_make_internal_notes()
    {
        Assert.Throws<RuleViolation>(() => TicketRules.AddReply(new Ticket { Status = TicketStatus.Closed }, UserRole.Admin, false, DateTime.UtcNow));
        Assert.Throws<RuleViolation>(() => TicketRules.AddReply(new Ticket(), UserRole.Customer, true, DateTime.UtcNow));
    }
    [Fact]
    public void Sla_is_breached_only_for_active_unmet_deadlines()
    {
        var now = DateTime.UtcNow;
        var t = new Ticket { ResponseDueAt = now.AddMinutes(-1), ResolutionDueAt = now.AddDays(1) };
        Assert.True(TicketRules.IsOverdue(t, now));
        t.FirstRespondedAt = now.AddMinutes(-2);
        Assert.False(TicketRules.IsOverdue(t, now));
        t.ResolutionDueAt = now.AddMinutes(-1);
        Assert.True(TicketRules.IsOverdue(t, now));
        t.Status = TicketStatus.Resolved;
        Assert.False(TicketRules.IsOverdue(t, now));
    }
    [Fact]
    public void Assignment_strategies_are_deterministic_and_respect_workload()
    {
        var a = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var b = Guid.Parse("00000000-0000-0000-0000-000000000002");
        (Guid Id, int Active)[] agents = [(a, 6), (b, 2)];
        Assert.Null(TicketRules.Assign(AssignmentStrategy.Manual, agents, 0));
        Assert.Equal(b, TicketRules.Assign(AssignmentStrategy.LeastBusy, agents, 0));
        Assert.Equal(a, TicketRules.Assign(AssignmentStrategy.RoundRobin, agents, 0));
        Assert.Equal(b, TicketRules.Assign(AssignmentStrategy.RoundRobin, agents, 1));
        Assert.Equal(a, TicketRules.Assign(AssignmentStrategy.RoundRobin, agents, 2));
        Assert.Null(TicketRules.Assign(AssignmentStrategy.LeastBusy, [], 0));
    }
}
