using Messenger.Domain.Support;

namespace Messenger.Domain.Tests;

public sealed class SupportTicketTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    private static SupportTicket Open(string id = "t1") =>
        SupportTicket.Open(id, "customer1", "Cannot log in", "App crashes on start", Now).Value!;

    [Fact]
    public void Open_validates_subject_and_description()
    {
        Assert.True(SupportTicket.Open("t1", "c1", "  Help  ", null, Now).IsSuccess);

        var empty = SupportTicket.Open("t2", "c1", "   ", null, Now);
        Assert.True(empty.IsFailure);
        Assert.Equal(SupportErrors.InvalidSubject, empty.Error);

        var longDesc = SupportTicket.Open("t3", "c1", "Help", new string('x', 2001), Now);
        Assert.True(longDesc.IsFailure);
        Assert.Equal(SupportErrors.InvalidDescription, longDesc.Error);
    }

    [Fact]
    public void Workflow_transitions()
    {
        var ticket = Open();

        Assert.Equal(TicketStatus.Open, ticket.Status);

        // Open -> Assigned requires Assign (agent path).
        Assert.True(ticket.Assign("agent1", Now).IsSuccess);
        Assert.Equal(TicketStatus.Assigned, ticket.Status);
        Assert.Equal("agent1", ticket.AssigneeId);

        Assert.True(ticket.SetStatus(TicketStatus.InProgress, byAgent: true, Now).IsSuccess);
        Assert.True(ticket.SetStatus(TicketStatus.Resolved, byAgent: true, Now).IsSuccess);

        // Resolved can reopen.
        Assert.True(ticket.SetStatus(TicketStatus.InProgress, byAgent: true, Now).IsSuccess);
        Assert.True(ticket.SetStatus(TicketStatus.Resolved, byAgent: true, Now).IsSuccess);

        Assert.True(ticket.SetStatus(TicketStatus.Closed, byAgent: false, Now).IsSuccess);
        Assert.True(ticket.IsClosed);
        Assert.NotNull(ticket.ClosedAt);

        // Closed is terminal.
        Assert.True(ticket.SetStatus(TicketStatus.InProgress, byAgent: true, Now).IsFailure);
        Assert.True(ticket.Assign("agent2", Now).IsFailure);
    }

    [Fact]
    public void Customer_cannot_advance_work_states()
    {
        var ticket = Open();

        Assert.Equal(SupportErrors.InvalidStatus,
            ticket.SetStatus(TicketStatus.InProgress, byAgent: false, Now).Error);
        Assert.Equal(SupportErrors.InvalidStatus,
            ticket.SetStatus(TicketStatus.Resolved, byAgent: false, Now).Error);

        // ...but may cancel their own open ticket.
        Assert.True(ticket.SetStatus(TicketStatus.Closed, byAgent: false, Now).IsSuccess);
    }

    [Fact]
    public void Work_states_require_an_assignee()
    {
        var ticket = Open();

        Assert.Equal(SupportErrors.AssigneeRequired,
            ticket.SetStatus(TicketStatus.Assigned, byAgent: true, Now).Error);
    }

    [Fact]
    public void Assign_rejects_customer_as_agent_and_closed_tickets()
    {
        var ticket = Open();

        Assert.Equal(SupportErrors.AgentOnly, ticket.Assign("customer1", Now).Error);

        ticket.SetStatus(TicketStatus.Closed, byAgent: false, Now);
        Assert.Equal(SupportErrors.AlreadyClosed, ticket.Assign("agent1", Now).Error);
    }

    [Fact]
    public void LinkChat_attaches_conversation_once()
    {
        var ticket = Open();
        ticket.Assign("agent1", Now);

        Assert.True(ticket.LinkChat("chat1", Now).IsSuccess);
        Assert.Equal("chat1", ticket.ChatId);
    }

    [Fact]
    public void Rehydrate_round_trips_state()
    {
        var ticket = Open();
        ticket.Assign("agent1", Now);
        ticket.LinkChat("chat1", Now);

        var revived = SupportTicket.Rehydrate(new TicketSnapshot(
            ticket.Id, ticket.CustomerId, ticket.Subject, ticket.Description,
            ticket.Status, ticket.AssigneeId, ticket.ChatId,
            ticket.CreatedAt, ticket.UpdatedAt, ticket.ClosedAt));

        Assert.Equal(TicketStatus.Assigned, revived.Status);
        Assert.Equal("agent1", revived.AssigneeId);
        Assert.Equal("chat1", revived.ChatId);
        Assert.Empty(revived.DomainEvents);
    }
}
