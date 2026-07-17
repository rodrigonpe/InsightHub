namespace InsightHub.Admin.Models;

public sealed class BotAnnouncementListItem
{
    public Guid Id { get; set; }
    public Guid? SourceAnnouncementId { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Reason { get; set; }

    public int Priority { get; set; }
    public bool StopBot { get; set; }

    public DateTime? StartsAt { get; set; }
    public DateTime? ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
