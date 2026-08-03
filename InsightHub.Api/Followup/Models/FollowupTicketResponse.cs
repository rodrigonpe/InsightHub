namespace InsightHub.Api.Dtos.Followup;

public class FollowupTicketResponse
{
    public string Provider { get; set; } = string.Empty;

    public string ProviderTicketId { get; set; } = string.Empty;

    public string? Subject { get; set; }

    public string? Status { get; set; }

    public string? Reason { get; set; }

    public string? RequesterName { get; set; }

    public string? OwnerName { get; set; }

    public string? OwnerTeam { get; set; }

    public DateTime? OpenedAt { get; set; }

    public DateTime? LastInteractionAt { get; set; }

    public decimal BusinessHoursElapsed { get; set; }

    public DateTime? NextFollowupAt { get; set; }

    public DateTime? LastFollowupSentAt { get; set; }

    public string FollowupStatus { get; set; } = string.Empty;
}