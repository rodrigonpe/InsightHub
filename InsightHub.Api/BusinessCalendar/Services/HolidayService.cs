using InsightHub.Api.BusinessCalendar.Models;
using InsightHub.Api.BusinessCalendar.Calculators;
using Npgsql;

namespace InsightHub.Api.BusinessCalendar.Services;
public class HolidayService
{
    private readonly IConfiguration _config;

    public HolidayService(IConfiguration config)
    {
        _config = config;
    }

    public async Task<HolidayResult?> FindHolidayAsync(
        DateOnly date,
        string? state = null,
        string? city = null)
    {
        var connectionString =
            _config.GetConnectionString("DefaultConnection");

        await using var connection =
            new NpgsqlConnection(connectionString);

        await connection.OpenAsync();

        const string sql = @"
            SELECT
                name,
                scope
            FROM holidays
            WHERE is_active = TRUE
            AND (
                (is_recurring = TRUE
                    AND month = @month
                    AND day = @day)
                OR
                (is_recurring = FALSE
                    AND holiday_date = @date)
            )
            AND (
                scope = 'NATIONAL'
                OR (scope = 'STATE' AND state = @state)
                OR (scope = 'CITY' AND state = @state AND city = @city)
            )
            ORDER BY
                CASE scope
                    WHEN 'CITY' THEN 1
                    WHEN 'STATE' THEN 2
                    WHEN 'NATIONAL' THEN 3
                END
            LIMIT 1;
        ";

        await using var command =
            new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue("month", date.Month);
        command.Parameters.AddWithValue("day", date.Day);
        command.Parameters.AddWithValue(
            "date",
            date.ToDateTime(TimeOnly.MinValue));

        command.Parameters.AddWithValue(
            "state",
            (object?)state ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "city",
            (object?)city ?? DBNull.Value);

        await using var reader =
            await command.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            return new HolidayResult
            {
                IsHoliday = true,
                Name = reader.GetString(0),
                Scope = reader.GetString(1)
            };
        }

        await reader.CloseAsync();

        var movableHoliday =
            MovableHolidaysCalculator
                .GetMovableHolidays(date.Year)
                .FirstOrDefault(h => h.Date == date);

        if (movableHoliday is not null)
        {
            return new HolidayResult
            {
                IsHoliday = true,
                Name = movableHoliday.Name,
                Scope = "NATIONAL"
            };
        }

        return null;
    }
}