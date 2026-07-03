namespace InsightHub.Admin.Models;

public class BusinessHourExceptionResponse
{
    public Guid Id { get; set; }
    public DateOnly ExceptionDate { get; set; }
    public bool IsOpen { get; set; }
    public string Schedule { get; set; } = string.Empty;
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public string? Reason { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }
}