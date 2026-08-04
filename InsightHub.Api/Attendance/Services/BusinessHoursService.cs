using InsightHub.Api.Attendance.Models;
using Npgsql;

namespace InsightHub.Api.BusinessCalendar.Services;

public class BusinessHoursService
{
    private readonly IConfiguration _configuration;

    public BusinessHoursService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<BusinessHoursResult> GetBusinessHoursAsync(
        DayOfWeek dayOfWeek)
    {
        var connectionString =
            _configuration.GetConnectionString("DefaultConnection");

        await using var connection =
            new NpgsqlConnection(connectionString);

        await connection.OpenAsync();

        const string sql = @"
            SELECT
                is_open,
                start_time,
                end_time
            FROM business_hours
            WHERE day_of_week = @dayOfWeek
              AND is_active = TRUE
            LIMIT 1;
        ";

        await using var command =
            new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue(
            "dayOfWeek",
            (short)dayOfWeek);

        await using var reader =
            await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return new BusinessHoursResult
            {
                IsConfigured = false
            };
        }

        return new BusinessHoursResult
        {
            IsConfigured = true,

            IsOpen =
                reader.GetBoolean(0),

            StartTime =
                reader.IsDBNull(1)
                    ? null
                    : TimeOnly.FromTimeSpan(
                        reader.GetTimeSpan(1)),

            EndTime =
                reader.IsDBNull(2)
                    ? null
                    : TimeOnly.FromTimeSpan(
                        reader.GetTimeSpan(2))
        };
    }
}