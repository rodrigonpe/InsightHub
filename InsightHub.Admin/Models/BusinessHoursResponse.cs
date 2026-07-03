namespace InsightHub.Admin.Models;

public class BusinessHourResponse
{
    public Guid Id { get; set; }
    public short DayOfWeek { get; set; }
    public string DayName { get; set; } = string.Empty;
    public bool IsOpen { get; set; }
    public string Schedule { get; set; } = string.Empty;
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public bool IsActive { get; set; }
}