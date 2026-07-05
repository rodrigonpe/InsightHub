namespace InsightHub.Admin.Models;

public class UpdateBusinessHourExceptionRequest
{
    public DateOnly ExceptionDate { get; set; }
    public bool IsOpen { get; set; }
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public string? Reason { get; set; }
    public string? Description { get; set; }
}