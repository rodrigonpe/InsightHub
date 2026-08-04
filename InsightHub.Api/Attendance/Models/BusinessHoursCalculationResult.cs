public class BusinessHoursCalculationResult
{
    public decimal TotalBusinessHours { get; set; }

    public bool IsCurrentlyBusinessTime { get; set; }

    public int BusinessDays { get; set; }

    public int NonBusinessDays { get; set; }

    public DateTime StartDate { get; set; }

    public DateTime EndDate { get; set; }

    public bool IsBusinessDay { get; set; }

    public string? Reason { get; set; }
}