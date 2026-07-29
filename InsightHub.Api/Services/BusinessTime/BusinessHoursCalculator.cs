namespace InsightHub.Api.Services.BusinessTime;

public class BusinessHoursCalculator
{
    private readonly HolidayService _holidayService;
    private readonly BusinessHourService _businessHourService;
    private readonly BusinessHourExceptionService _exceptionService;

    public BusinessHoursCalculator(
        HolidayService holidayService,
        BusinessHourService businessHourService,
        BusinessHourExceptionService exceptionService)
    {
        _holidayService = holidayService;
        _businessHourService = businessHourService;
        _exceptionService = exceptionService;
    }

    public async Task<BusinessHoursCalculationResult> CalculateAsync(
        DateTime start,
        DateTime end)
    {
        if (end <= start)
        {
            return new BusinessHoursCalculationResult();
        }

        decimal totalHours = 0;
        int businessDays = 0;
        int nonBusinessDays = 0;

        var current = start;

        while (current.Date <= end.Date)
        {
            var date = current.Date;

            // Aqui vamos reutilizar os serviços que você já possui
            // para descobrir:
            //
            // - se é feriado;
            // - qual horário de atendimento do dia;
            // - se existe exceção para esse dia.
            //
            // Depois calculamos apenas o intervalo útil.

            current = current.AddDays(1);
        }

        return new BusinessHoursCalculationResult
        {
            TotalBusinessHours = totalHours,
            BusinessDays = businessDays,
            NonBusinessDays = nonBusinessDays,
            IsCurrentlyBusinessTime = false
        };
    }
}