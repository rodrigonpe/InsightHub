namespace InsightHub.Api.BusinessCalendar.Models;

public class BusinessDayResult
{
    public bool IsBusinessDay { get; init; }

    public bool IsWeekend { get; init; }

    public bool IsHoliday { get; init; }

    public string? HolidayName { get; init; }

    public string? Reason { get; init; }
}