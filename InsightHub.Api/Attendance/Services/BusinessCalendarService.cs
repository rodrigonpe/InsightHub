using InsightHub.Api.BusinessCalendar.Models;
using InsightHub.Api.BusinessCalendar.Services;

namespace InsightHub.Api.Attendance.Services;

public class BusinessCalendarService
{
    private readonly HolidayService _holidayService;

    public BusinessCalendarService(HolidayService holidayService)
    {
        _holidayService = holidayService;
    }

    public async Task<BusinessDayResult> IsBusinessDayAsync(
        DateOnly date,
        string? state = null,
        string? city = null)
    {
        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return new BusinessDayResult
            {
                IsBusinessDay = false,
                IsWeekend = true,
                IsHoliday = false,
                HolidayName = null,
                Reason = "WEEKEND"
            };
        }

        var holiday = await _holidayService.FindHolidayAsync(
            date,
            state,
            city);

        if (holiday is not null)
        {
            return new BusinessDayResult
            {
                IsBusinessDay = false,
                IsWeekend = false,
                IsHoliday = true,
                HolidayName = holiday.Name,
                Reason = "HOLIDAY"
            };
        }

        return new BusinessDayResult
        {
            IsBusinessDay = true,
            IsWeekend = false,
            IsHoliday = false,
            HolidayName = null,
            Reason = null
        };
    }
}