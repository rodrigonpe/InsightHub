namespace InsightHub.Services;
public static class MovableHolidaysCalculator
{
    public static List<MovableHoliday> GetMovableHolidays(int year)
    {
        var easter = EasterCalculator.CalculateEaster(year);

        return new List<MovableHoliday>
        {
            new("Carnaval", easter.AddDays(-47)),
            new("Sexta-feira Santa", easter.AddDays(-2)),
            new("Páscoa", easter),
            new("Corpus Christi", easter.AddDays(60))
        };
    }
}
public record MovableHoliday(string Name, DateOnly Date);