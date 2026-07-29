namespace InsightHub.Api.Models.Followup;

public class IntegrationProvider
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? BaseUrl { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }
}