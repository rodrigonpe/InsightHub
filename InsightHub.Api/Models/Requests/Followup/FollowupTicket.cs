namespace InsightHub.Api.Models.Followup;

public class FollowupTicket
{
    public Guid Id { get; set; }

    public Guid ProviderId { get; set; }

    public string ProviderTicketId { get; set; } = string.Empty;

    public string? Subject { get; set; }

    public string? ProviderStatus { get; set; }

    public string? ProviderReason { get; set; }

    public string? RequesterName { get; set; }

    public string? OwnerName { get; set; }

    public string? OwnerTeam { get; set; }

    public DateTime? OpenedAt { get; set; }

    public DateTime? LastInteractionAt { get; set; }

    public decimal BusinessHoursElapsed { get; set; }

    public DateTime? NextFollowupAt { get; set; }

    public DateTime? LastFollowupSentAt { get; set; }

    public string FollowupStatus { get; set; } = "MONITORING";

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}