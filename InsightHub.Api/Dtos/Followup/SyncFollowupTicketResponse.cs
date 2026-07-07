namespace InsightHub.Api.Dtos.Followup;

public class SyncFollowupTicketResponse
{
    public string Message { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;

    public string ProviderTicketId { get; set; } = string.Empty;

    public string? Subject { get; set; }

    public string? Status { get; set; }

    public string? Reason { get; set; }

    public string FollowupStatus { get; set; } = string.Empty;
}