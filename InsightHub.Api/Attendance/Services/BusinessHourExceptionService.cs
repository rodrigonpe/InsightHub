using InsightHub.Api.Attendance.Models;
using Npgsql;

namespace InsightHub.Api.BusinessCalendar.Services;

public class BusinessHourExceptionService
{
    private readonly IConfiguration _configuration;

    public BusinessHourExceptionService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<BusinessSchedule?> GetExceptionAsync(DateOnly date)
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
                end_time,
                reason
            FROM business_hour_exceptions
            WHERE exception_date = @date
              AND is_active = TRUE
            LIMIT 1;
        ";

        await using var command =
            new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue(
            "date",
            date.ToDateTime(TimeOnly.MinValue));

        await using var reader =
            await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        return new BusinessSchedule
        {
            IsOpen = reader.GetBoolean(0),

            StartTime =
                reader.IsDBNull(1)
                    ? null
                    : TimeOnly.FromTimeSpan(reader.GetTimeSpan(1)),

            EndTime =
                reader.IsDBNull(2)
                    ? null
                    : TimeOnly.FromTimeSpan(reader.GetTimeSpan(2)),

            Reason =
                reader.IsDBNull(3)
                    ? null
                    : reader.GetString(3)
        };
    }
}