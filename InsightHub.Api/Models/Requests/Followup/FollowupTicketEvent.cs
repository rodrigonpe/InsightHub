namespace InsightHub.Api.Models.Followup;

public class FollowupTicketEvent
{
    public Guid Id { get; set; }

    public Guid FollowupTicketId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string? Description { get; set; }

    public decimal? BusinessHoursElapsed { get; set; }

    public DateTime CreatedAt { get; set; }
}