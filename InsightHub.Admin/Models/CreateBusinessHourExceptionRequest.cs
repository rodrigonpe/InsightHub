namespace InsightHub.Admin.Models;

public class CreateBusinessHourExceptionRequest
{
    public DateOnly ExceptionDate { get; set; }
    public bool IsOpen { get; set; } = true;
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public string? Reason { get; set; }
    public string? Description { get; set; }
    public Guid CreatedByUserId { get; set; }
}