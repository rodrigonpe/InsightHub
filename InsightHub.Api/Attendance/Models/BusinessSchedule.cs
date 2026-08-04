namespace InsightHub.Api.Attendance.Models;

public class BusinessSchedule
{
    public bool IsOpen { get; set; }

    public TimeOnly? StartTime { get; set; }

    public TimeOnly? EndTime { get; set; }

    public string? Reason { get; set; }
}