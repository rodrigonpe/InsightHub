namespace InsightHub.Api.Attendance.Models;

public class BusinessHoursResult
{
    public bool IsOpen { get; set; }

    public TimeOnly? StartTime { get; set; }

    public TimeOnly? EndTime { get; set; }

    public bool IsConfigured { get; set; }
}