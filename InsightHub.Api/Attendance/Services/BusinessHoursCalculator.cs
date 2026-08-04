using InsightHub.Api.BusinessCalendar.Services;
using InsightHub.Api.Attendance.Models;

namespace InsightHub.Api.Attendance.Services;

public class BusinessHoursCalculator
{
    private readonly BusinessCalendarService _calendarService;
    private readonly BusinessHoursService _businessHoursService;
    private readonly BusinessHourExceptionService _exceptionService;

    public BusinessHoursCalculator(
        BusinessCalendarService calendarService,
        BusinessHoursService businessHourService,
        BusinessHourExceptionService exceptionService)
    {
        _calendarService = calendarService;
        _businessHoursService = businessHourService;
        _exceptionService = exceptionService;
    }
        public async Task<BusinessHoursCalculationResult> CalculateAsync(
        DateTime start,
        DateTime end,
        string? state = null,
        string? city = null)
    {
        if (end <= start)
            throw new ArgumentException("A data final deve ser maior que a inicial.");

        var result = new BusinessHoursCalculationResult
        {
            StartDate = start,
            EndDate = end
        };

        // implementação virá aqui

        return result;
    }
        private async Task<BusinessHoursResult> GetScheduleAsync(
        DateOnly date,
        string? state,
        string? city)
    {
        // 1) Verifica se existe exceção para o dia
        var exception =
            await _exceptionService.GetExceptionAsync(date);

        if (exception is not null)
        {
            return new BusinessHoursResult
            {
                IsConfigured = true,
                IsOpen = exception.IsOpen,
                StartTime = exception.StartTime,
                EndTime = exception.EndTime
            };
        }

        // 2) Verifica se é dia útil
        var businessDay =
            await _calendarService.IsBusinessDayAsync(
                date,
                state,
                city);

        if (!businessDay.IsBusinessDay)
        {
            return new BusinessHoursResult
            {
                IsConfigured = true,
                IsOpen = false
            };
        }

        // 3) Busca o horário padrão
        return await _businessHoursService.GetBusinessHoursAsync(
            date.DayOfWeek);
    }
}