using InsightHub.Api.Attendance.Models;
using InsightHub.Api.BusinessCalendar.Services;

namespace InsightHub.Api.Attendance.Calculators;

public class BusinessHoursCalculator
{
    private readonly BusinessCalendarService _calendar;

    public BusinessHoursCalculator(
        BusinessCalendarService calendar)
    {
        _calendar = calendar;
    }

    public async Task<BusinessHoursCalculationResult> CalculateAsync(
        DateTime start,
        DateTime end,
        string? state = null,
        string? city = null)
    {
        if (end < start)
            throw new ArgumentException(
                "A data final deve ser maior que a inicial.");

        decimal totalHours = 0;
        int businessDays = 0;
        int nonBusinessDays = 0;

        var current = start.Date;

        while (current <= end.Date)
        {
            var businessDay =
                await _calendar.IsBusinessDayAsync(
                    DateOnly.FromDateTime(current),
                    state,
                    city);

            if (businessDay.IsBusinessDay)
            {
                businessDays++;

                var begin =
                    current == start.Date
                        ? start
                        : current.AddHours(8);

                var finish =
                    current == end.Date
                        ? end
                        : current.AddHours(17);

                if (finish > begin)
                {
                    totalHours +=
                        (decimal)(finish - begin).TotalHours;
                }
            }
            else
            {
                nonBusinessDays++;
            }

            current = current.AddDays(1);
        }

        var nowBusiness =
            (await _calendar.IsBusinessDayAsync(
                DateOnly.FromDateTime(DateTime.Now),
                state,
                city)).IsBusinessDay;

        return new BusinessHoursCalculationResult
        {
            StartDate = start,
            EndDate = end,
            TotalBusinessHours = totalHours,
            BusinessDays = businessDays,
            NonBusinessDays = nonBusinessDays,
            IsCurrentlyBusinessTime = nowBusiness
        };
    }
}