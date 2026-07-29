namespace InsightHub.Api.Services.BusinessTime;

public class BusinessHoursCalculationResult
{
    public decimal TotalBusinessHours { get; set; }

    public bool IsCurrentlyBusinessTime { get; set; }

    public int BusinessDays { get; set; }

    public int NonBusinessDays { get; set; }
}