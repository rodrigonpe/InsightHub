namespace InsightHub.Api.Models.Followup;

public class FollowupRule
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public Guid ProviderId { get; set; }

    public string ProviderStatus { get; set; } = string.Empty;

    public string? ProviderReason { get; set; }

    public int BusinessHoursToWait { get; set; }

    public string TemplateName { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}