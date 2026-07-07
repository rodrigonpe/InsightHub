namespace InsightHub.Api.Models.Providers;

public class ProviderTicket
{
    public string ProviderTicketId { get; set; } = string.Empty;

    public string? Subject { get; set; }

    public string? Status { get; set; }

    public string? Reason { get; set; }

    public string? RequesterName { get; set; }

    public string? OwnerName { get; set; }

    public string? OwnerTeam { get; set; }

    public DateTime? OpenedAt { get; set; }

    public DateTime? LastInteractionAt { get; set; }
}