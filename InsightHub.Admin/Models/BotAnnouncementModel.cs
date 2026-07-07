public class BotAnnouncementModel
{
    public Guid Id { get; set; }

    public string Title { get; set; } = "";

    public string Type { get; set; } = "INFO";

    public string Status { get; set; } = "ACTIVE";

    public string? Reason { get; set; }

    public int Priority { get; set; }

    public bool StopBot { get; set; }

    public string MessageHtml { get; set; } = "";

    public string? MessageText { get; set; }

    public DateTime? StartsAt { get; set; }

    public DateTime? ExpiresAt { get; set; }
}