using InsightHub.Services;
using Npgsql;
internal class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var app = builder.Build();

        app.MapGet("/health", () => new
        {
            status = "ok",
            service = "InsightHub",
            version = "1.0.0"
        });
        app.MapGet("/about", () => new
        {
            name = "InsightHub",
            description = "Customer Support Intelligence Platform",
        });
        app.MapGet("/db-test", async (IConfiguration config) =>
        {
            var connectionString = config.GetConnectionString("DefaultConnection");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            return Results.Ok(new
            {
                message = "Conexão com PostgreSQL realizada com sucesso!"
            });
        });
        app.MapGet("/holidays", async (int? year, IConfiguration config) =>
        {
            var referenceYear = year ?? DateTime.Now.Year;
            var connectionString = config.GetConnectionString("DefaultConnection");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            const string sql = @"
        SELECT
            id,
            name,
            description,
            holiday_date,
            month,
            day,
            is_recurring,
            scope,
            state,
            city,
            is_active,
            created_at,
            updated_at
        FROM holidays
        ORDER BY
            month NULLS LAST,
            day NULLS LAST,
            holiday_date NULLS LAST;
    ";

            await using var command = new NpgsqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync();

            var holidays = new List<object>();

            // Adiciona feriados móveis primeiro
            var movableHolidays = MovableHolidaysCalculator.GetMovableHolidays(referenceYear);

            foreach (var movableHoliday in movableHolidays)
            {
                holidays.Add(new
                {
                    id = Guid.Empty,
                    name = movableHoliday.Name,
                    description = "Feriado nacional móvel calculado automaticamente",
                    holidayDate = movableHoliday.Date.ToString("yyyy-MM-dd"),
                    month = (short?)movableHoliday.Date.Month,
                    day = (short?)movableHoliday.Date.Day,
                    isRecurring = false,
                    scope = "NATIONAL",
                    state = (string?)null,
                    city = (string?)null,
                    isActive = true,
                    createdAt = (DateTime?)null,
                    updatedAt = (DateTime?)null,
                    source = "CALCULATED"
                });
            }

            // Depois adiciona os do banco
            while (await reader.ReadAsync())
            {
                holidays.Add(new
                {
                    id = reader.GetGuid(0),
                    name = reader.GetString(1),
                    description = reader.IsDBNull(2) ? null : reader.GetString(2),
                    holidayDate = reader.IsDBNull(3) ? null : reader.GetDateTime(3).ToString("yyyy-MM-dd"),
                    month = reader.IsDBNull(4) ? (short?)null : reader.GetInt16(4),
                    day = reader.IsDBNull(5) ? (short?)null : reader.GetInt16(5),
                    isRecurring = reader.GetBoolean(6),
                    scope = reader.GetString(7),
                    state = reader.IsDBNull(8) ? null : reader.GetString(8),
                    city = reader.IsDBNull(9) ? null : reader.GetString(9),
                    isActive = reader.GetBoolean(10),
                    createdAt = reader.GetDateTime(11),
                    updatedAt = reader.GetDateTime(12),
                    source = "DATABASE"
                });
            }

            return Results.Ok(holidays);
        });
        app.MapGet("/holidays/{id:guid}", async (Guid id, IConfiguration config) =>
        {
            var connectionString = config.GetConnectionString("DefaultConnection");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            const string sql = @"
        SELECT
            id,
            name,
            description,
            holiday_date,
            month,
            day,
            is_recurring,
            scope,
            state,
            city,
            is_active,
            created_at,
            updated_at
        FROM holidays
        WHERE id = @id;
    ";

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", id);

            await using var reader = await command.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                return Results.NotFound(new
                {
                    error = "Feriado não encontrado."
                });
            }

            var holiday = new
            {
                id = reader.GetGuid(0),
                name = reader.GetString(1),
                description = reader.IsDBNull(2) ? null : reader.GetString(2),
                holidayDate = reader.IsDBNull(3) ? null : reader.GetDateTime(3).ToString("yyyy-MM-dd"),
                month = reader.IsDBNull(4) ? (short?)null : reader.GetInt16(4),
                day = reader.IsDBNull(5) ? (short?)null : reader.GetInt16(5),
                isRecurring = reader.GetBoolean(6),
                scope = reader.GetString(7),
                state = reader.IsDBNull(8) ? null : reader.GetString(8),
                city = reader.IsDBNull(9) ? null : reader.GetString(9),
                isActive = reader.GetBoolean(10),
                createdAt = reader.GetDateTime(11),
                updatedAt = reader.GetDateTime(12)
            };

            return Results.Ok(holiday);
        });
        app.MapGet("/calendar/is-business-day", async (DateOnly date, string? state, string? city, IConfiguration config) =>
        {
            /*
            Verifica se a data informada é sábado ou domingo.
            Dias de fim de semana não são considerados dias úteis.
            */
            var dayOfWeek = date.DayOfWeek;

            if (dayOfWeek == DayOfWeek.Saturday || dayOfWeek == DayOfWeek.Sunday)
            {
                return Results.Ok(new
                {
                    date = date.ToString("yyyy-MM-dd"),
                    isBusinessDay = false,
                    isWeekend = true,
                    isHoliday = false,
                    holidayName = (string?)null,
                    scope = (string?)null
                });
            }

            var connectionString = config.GetConnectionString("DefaultConnection");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            /*
            Consulta os feriados cadastrados no banco de dados.
            Abrange feriados nacionais, estaduais, municipais e datas específicas.
            */
            const string sql = @"
        SELECT
            name,
            scope
        FROM holidays
        WHERE is_active = TRUE
          AND (
                (is_recurring = TRUE AND month = @month AND day = @day)
                OR
                (is_recurring = FALSE AND holiday_date = @date)
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

            await using var command = new NpgsqlCommand(sql, connection);

            /*
            Adiciona os parâmetros utilizados pela consulta SQL.
            */
            command.Parameters.AddWithValue("month", date.Month);
            command.Parameters.AddWithValue("day", date.Day);
            command.Parameters.AddWithValue("date", date.ToDateTime(TimeOnly.MinValue));
            command.Parameters.AddWithValue("state", (object?)state ?? DBNull.Value);
            command.Parameters.AddWithValue("city", (object?)city ?? DBNull.Value);

            await using var reader = await command.ExecuteReaderAsync();

            /*
            Encontrou um feriado cadastrado no banco.
            A data não deve ser considerada dia útil.
            */
            if (await reader.ReadAsync())
            {
                return Results.Ok(new
                {
                    date = date.ToString("yyyy-MM-dd"),
                    isBusinessDay = false,
                    isWeekend = false,
                    isHoliday = true,
                    holidayName = reader.GetString(0),
                    scope = reader.GetString(1)
                });
            }

            /*
            Verifica feriados móveis calculados dinamicamente a partir da Páscoa.
            Ex.: Carnaval, Sexta-feira Santa e Corpus Christi.
            */
            var movableHoliday = MovableHolidaysCalculator
                .GetMovableHolidays(date.Year)
                .FirstOrDefault(h => h.Date == date);

            /*
            Encontrou um feriado móvel.
            A data não deve ser considerada dia útil.
            */
            if (movableHoliday is not null)
            {
                return Results.Ok(new
                {
                    date = date.ToString("yyyy-MM-dd"),
                    isBusinessDay = false,
                    isWeekend = false,
                    isHoliday = true,
                    holidayName = movableHoliday.Name,
                    scope = "NATIONAL"
                });
            }

            /*
            Não foi identificado fim de semana nem feriado.
            A data é considerada dia útil.
            */
            return Results.Ok(new
            {
                date = date.ToString("yyyy-MM-dd"),
                isBusinessDay = true,
                isWeekend = false,
                isHoliday = false,
                holidayName = (string?)null,
                scope = (string?)null
            });
        });
        /*app.MapGet("/calendar/today", () => new
        {
            date = "2026-06-26",
            DayOfWeek = "Sunday",
        });
        app.MapGet("/calendar/is-bussines-day?", () => new
        {
            date = "2026-06-26",
            DayOfWeek = "Sunday",
        }); */
        app.Run();
    }
}