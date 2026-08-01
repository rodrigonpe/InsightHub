using Npgsql;
using System.Security.Claims;
using InsightHub.Api.Services.Calendar;

public static class AttendanceEndpoints
{
    public static IEndpointRouteBuilder MapAttendanceEndpoints(this IEndpointRouteBuilder app)
    {  
        app.MapGet("/attendance/availability", async (DateTime? datetime, string? state, string? city, IConfiguration config) =>
        {
            var currentDateTime = datetime ?? DateTime.Now;
            var currentDate = DateOnly.FromDateTime(currentDateTime);
            var currentTime = TimeOnly.FromDateTime(currentDateTime);
            var dayOfWeek = (int)currentDate.DayOfWeek;

            var connectionString = config.GetConnectionString("DefaultConnection");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            /*
            Primeiro verifica se existe uma exceção de horário para a data.
            A exceção tem prioridade sobre o horário padrão.
            */
            const string exceptionSql = @"
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

            await using (var exceptionCommand = new NpgsqlCommand(exceptionSql, connection))
            {
                exceptionCommand.Parameters.AddWithValue("date", currentDate.ToDateTime(TimeOnly.MinValue));

                await using var exceptionReader = await exceptionCommand.ExecuteReaderAsync();

                if (await exceptionReader.ReadAsync())
                {
                    var isOpen = exceptionReader.GetBoolean(0);
                    var startTime = exceptionReader.IsDBNull(1) ? (TimeOnly?)null : TimeOnly.FromTimeSpan(exceptionReader.GetTimeSpan(1));
                    var endTime = exceptionReader.IsDBNull(2) ? (TimeOnly?)null : TimeOnly.FromTimeSpan(exceptionReader.GetTimeSpan(2));
                    var reason = exceptionReader.IsDBNull(3) ? null : exceptionReader.GetString(3);

                    var available = isOpen
                        && startTime.HasValue
                        && endTime.HasValue
                        && currentTime >= startTime.Value
                        && currentTime <= endTime.Value;

                    return Results.Ok(new
                    {
                        available,
                        date = currentDate.ToString("yyyy-MM-dd"),
                        time = currentTime.ToString("HH:mm"),
                        scheduleType = "SPECIAL",
                        startTime = startTime?.ToString("HH:mm"),
                        endTime = endTime?.ToString("HH:mm"),
                        reason = available ? null : "OUTSIDE_SPECIAL_BUSINESS_HOURS",
                        description = reason
                    });
                }
            }

            /*
            Se não houver exceção, verifica se a data é dia útil.
            Reaplica a mesma lógica básica do calendário: fim de semana, feriados do banco e feriados móveis.
            */
            var isWeekend = currentDate.DayOfWeek == DayOfWeek.Saturday || currentDate.DayOfWeek == DayOfWeek.Sunday;
            var isHoliday = false;
            string? holidayName = null;

            const string holidaySql = @"
                SELECT
                    name
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

            await using (var holidayCommand = new NpgsqlCommand(holidaySql, connection))
            {
                holidayCommand.Parameters.AddWithValue("month", currentDate.Month);
                holidayCommand.Parameters.AddWithValue("day", currentDate.Day);
                holidayCommand.Parameters.AddWithValue("date", currentDate.ToDateTime(TimeOnly.MinValue));
                holidayCommand.Parameters.AddWithValue("state", (object?)state ?? DBNull.Value);
                holidayCommand.Parameters.AddWithValue("city", (object?)city ?? DBNull.Value);

                await using var holidayReader = await holidayCommand.ExecuteReaderAsync();

                if (await holidayReader.ReadAsync())
                {
                    isHoliday = true;
                    holidayName = holidayReader.GetString(0);
                }
            }

            if (!isHoliday)
            {
                var movableHoliday = MovableHolidaysCalculator
                    .GetMovableHolidays(currentDate.Year)
                    .FirstOrDefault(h => h.Date == currentDate);

                if (movableHoliday is not null)
                {
                    isHoliday = true;
                    holidayName = movableHoliday.Name;
                }
            }

            if (isWeekend || isHoliday)
            {
                return Results.Ok(new
                {
                    available = false,
                    date = currentDate.ToString("yyyy-MM-dd"),
                    time = currentTime.ToString("HH:mm"),
                    isBusinessDay = false,
                    isWeekend,
                    isHoliday,
                    holidayName,
                    scheduleType = isHoliday ? "HOLIDAY" : "WEEKEND",
                    startTime = (string?)null,
                    endTime = (string?)null,
                    reason = isHoliday ? "HOLIDAY" : "WEEKEND"
                });
            }

            /*
            Se for dia útil e não houver exceção, aplica o horário padrão da semana.
            */
            const string businessHourSql = @"
                SELECT
                    is_open,
                    start_time,
                    end_time
                FROM business_hours
                WHERE day_of_week = @dayOfWeek
                AND is_active = TRUE
                LIMIT 1;
            ";

            await using var businessHourCommand = new NpgsqlCommand(businessHourSql, connection);
            businessHourCommand.Parameters.AddWithValue("dayOfWeek", dayOfWeek);

            await using var businessHourReader = await businessHourCommand.ExecuteReaderAsync();

            if (!await businessHourReader.ReadAsync())
            {
                return Results.Ok(new
                {
                    available = false,
                    date = currentDate.ToString("yyyy-MM-dd"),
                    time = currentTime.ToString("HH:mm"),
                    isBusinessDay = true,
                    scheduleType = "DEFAULT_NOT_CONFIGURED",
                    reason = "BUSINESS_HOURS_NOT_CONFIGURED"
                });
            }

            var defaultIsOpen = businessHourReader.GetBoolean(0);
            var defaultStartTime = businessHourReader.IsDBNull(1) ? (TimeOnly?)null : TimeOnly.FromTimeSpan(businessHourReader.GetTimeSpan(1));
            var defaultEndTime = businessHourReader.IsDBNull(2) ? (TimeOnly?)null : TimeOnly.FromTimeSpan(businessHourReader.GetTimeSpan(2));

            var defaultAvailable = defaultIsOpen
                && defaultStartTime.HasValue
                && defaultEndTime.HasValue
                && currentTime >= defaultStartTime.Value
                && currentTime <= defaultEndTime.Value;

            return Results.Ok(new
            {
                available = defaultAvailable,
                date = currentDate.ToString("yyyy-MM-dd"),
                time = currentTime.ToString("HH:mm"),
                isBusinessDay = true,
                isWeekend = false,
                isHoliday = false,
                holidayName = (string?)null,
                scheduleType = "DEFAULT",
                startTime = defaultStartTime?.ToString("HH:mm"),
                endTime = defaultEndTime?.ToString("HH:mm"),
                reason = defaultAvailable ? null : "OUTSIDE_BUSINESS_HOURS"
            });
        })
        .WithTags("Availability");
        app.MapPost("/attendance/exceptions", async (CreateBusinessHourExceptionRequest request, IConfiguration config, HttpContext httpContext) =>
            {
                var id = Guid.NewGuid();

                var userId = Guid.Parse(
                httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

                var today = DateOnly.FromDateTime(DateTime.Now);
                var now = TimeOnly.FromDateTime(DateTime.Now);

                if (request.ExceptionDate < today)
                {
                    return Results.BadRequest(new
                    {
                        message = "Não é possível cadastrar exceções para datas passadas."
                    });
                }

                if (!request.IsOpen)
                {
                    return Results.BadRequest(new
                    {
                        message = "Para exceções de atendimento, informe um horário inicial e final."
                    });
                }

                if (!request.StartTime.HasValue || !request.EndTime.HasValue)
                {
                    return Results.BadRequest(new
                    {
                        message = "Informe o horário inicial e final da exceção."
                    });
                }

                if (request.StartTime >= request.EndTime)
                {
                    return Results.BadRequest(new
                    {
                        message = "O horário inicial deve ser menor que o horário final."
                    });
                }

                if (request.ExceptionDate == today && request.EndTime.Value <= now)
                {
                    return Results.BadRequest(new
                    {
                        message = "Não é possível cadastrar uma exceção cujo horário final já passou."
                    });
                }

                var connectionString = config.GetConnectionString("DefaultConnection");

                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();

                var dayOfWeek = (int)request.ExceptionDate.DayOfWeek;

                const string businessHourSql = @"
                    SELECT is_open, start_time, end_time
                    FROM business_hours
                    WHERE day_of_week = @dayOfWeek
                    AND is_active = TRUE
                    LIMIT 1;
                ";

                await using (var businessHourCommand = new NpgsqlCommand(businessHourSql, connection))
                {
                    businessHourCommand.Parameters.AddWithValue("dayOfWeek", dayOfWeek);

                    await using var businessHourReader = await businessHourCommand.ExecuteReaderAsync();

                    if (!await businessHourReader.ReadAsync())
                    {
                        return Results.BadRequest(new
                        {
                            message = "Não há horário padrão configurado para o dia informado."
                        });
                    }

                    var defaultIsOpen = businessHourReader.GetBoolean(0);

                    var defaultStartTime = businessHourReader.IsDBNull(1)
                        ? (TimeOnly?)null
                        : TimeOnly.FromTimeSpan(businessHourReader.GetTimeSpan(1));

                    var defaultEndTime = businessHourReader.IsDBNull(2)
                        ? (TimeOnly?)null
                        : TimeOnly.FromTimeSpan(businessHourReader.GetTimeSpan(2));

                    if (!defaultIsOpen || !defaultStartTime.HasValue || !defaultEndTime.HasValue)
                    {
                        return Results.BadRequest(new
                        {
                            message = "Não há atendimento padrão no dia informado."
                        });
                    }

                    if (request.StartTime.Value < defaultStartTime.Value ||
                        request.EndTime.Value > defaultEndTime.Value)
                    {
                        return Results.BadRequest(new
                        {
                            message = $"O horário especial deve estar dentro do horário padrão do dia: {defaultStartTime:HH\\:mm} às {defaultEndTime:HH\\:mm}."
                        });
                    }
                }

                const string checkSql = @"
                    SELECT COUNT(*)
                    FROM business_hour_exceptions
                    WHERE exception_date = @exceptionDate
                    AND is_active = TRUE;
                ";

                await using var checkCommand = new NpgsqlCommand(checkSql, connection);
                checkCommand.Parameters.AddWithValue("exceptionDate", request.ExceptionDate.ToDateTime(TimeOnly.MinValue));

                var existingCount = (long)(await checkCommand.ExecuteScalarAsync() ?? 0);

                if (existingCount > 0)
                {
                    return Results.Conflict(new
                    {
                        message = "Já existe uma exceção ativa cadastrada para esta data."
                    });
                }

                const string sql = @"
                    INSERT INTO business_hour_exceptions (
                        id,
                        exception_date,
                        is_open,
                        start_time,
                        end_time,
                        reason,
                        description,
                        is_active,
                        created_by_user_id
                    )
                    VALUES (
                        @id,
                        @exceptionDate,
                        @isOpen,
                        @startTime,
                        @endTime,
                        @reason,
                        @description,
                        TRUE,
                        @createdByUserId
                    );
                ";

                await using var command = new NpgsqlCommand(sql, connection);

                command.Parameters.AddWithValue("id", id);
                command.Parameters.AddWithValue("exceptionDate", request.ExceptionDate.ToDateTime(TimeOnly.MinValue));
                command.Parameters.AddWithValue("isOpen", request.IsOpen);
                command.Parameters.AddWithValue("startTime", request.StartTime.Value.ToTimeSpan());
                command.Parameters.AddWithValue("endTime", request.EndTime.Value.ToTimeSpan());
                command.Parameters.AddWithValue("reason", (object?)request.Reason ?? DBNull.Value);
                command.Parameters.AddWithValue("description", (object?)request.Description ?? DBNull.Value);
                command.Parameters.AddWithValue("createdByUserId", userId);

                await command.ExecuteNonQueryAsync();

                return Results.Created($"/attendance/exceptions/{id}", new
                {
                    id,
                    message = "Exceção de horário cadastrada com sucesso."
                });
            })
        .WithTags("Attendance - Exceptions");
        app.MapGet("/attendance/exceptions", async (bool? includeInactive,IConfiguration config) =>
        {
            var connectionString = config.GetConnectionString("DefaultConnection");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            const string sql = @"
                SELECT
                    id,
                    exception_date,
                    is_open,
                    start_time,
                    end_time,
                    reason,
                    description,
                    is_active,
                    created_at,
                    updated_at
                FROM business_hour_exceptions
                WHERE (@includeInactive = TRUE OR is_active = TRUE)
                ORDER BY exception_date;
            ";
            
            await using var command = new NpgsqlCommand(sql, connection);

            command.Parameters.AddWithValue("includeInactive", includeInactive == true);
            
            await using var reader = await command.ExecuteReaderAsync();

            var exceptions = new List<object>();

            while (await reader.ReadAsync())
            {
                var isOpen = reader.GetBoolean(2);

                var startTime = reader.IsDBNull(3)
                    ? (TimeOnly?)null
                    : TimeOnly.FromTimeSpan(reader.GetTimeSpan(3));

                var endTime = reader.IsDBNull(4)
                    ? (TimeOnly?)null
                    : TimeOnly.FromTimeSpan(reader.GetTimeSpan(4));

                var exceptionDate = DateOnly.FromDateTime(reader.GetDateTime(1));
                var today = DateOnly.FromDateTime(DateTime.Today);

                var situation = !reader.GetBoolean(7)
                    ? "INACTIVE"
                    : exceptionDate < today
                        ? "EXPIRED"
                        : exceptionDate == today
                            ? "CURRENT"
                            : "SCHEDULED";

                var situationLabel = situation switch
                {
                    "INACTIVE" => "Inativa",
                    "EXPIRED" => "Expirada",
                    "CURRENT" => "Vigente",
                    "SCHEDULED" => "Agendada",
                    _ => "Desconhecida"
                };

                exceptions.Add(new
                {
                    id = reader.GetGuid(0),
                    exceptionDate = exceptionDate,
                    isOpen,
                    schedule = isOpen
                        ? $"{startTime:HH\\:mm} às {endTime:HH\\:mm}"
                        : "Fechado",
                    startTime,
                    endTime,
                    reason = reader.IsDBNull(5) ? null : reader.GetString(5),
                    description = reader.IsDBNull(6) ? null : reader.GetString(6),
                    isActive = reader.GetBoolean(7),
                    situation = situation,
                    situationLabel = situationLabel,
                    createdAt = reader.GetDateTime(8),
                    updatedAt = reader.IsDBNull(9) ? (DateTime?)null : reader.GetDateTime(9)
                });
            }

            return Results.Ok(exceptions);
        })
        .RequireAuthorization()
        .WithTags("Attendance - Exceptions");
        app.MapGet("/attendance/exceptions/{id:guid}", async (Guid id, IConfiguration config) =>
        {
            var connectionString = config.GetConnectionString("DefaultConnection");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            const string sql = @"
                SELECT
                    id,
                    exception_date,
                    is_open,
                    start_time,
                    end_time,
                    reason,
                    description,
                    is_active,
                    created_at,
                    updated_at
                FROM business_hour_exceptions
                WHERE id = @id;
            ";

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", id);

            await using var reader = await command.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                return Results.NotFound(new
                {
                    message = "Exceção de horário não encontrada."
                });
            }

            var isOpen = reader.GetBoolean(2);

            var startTime = reader.IsDBNull(3)
                ? (TimeOnly?)null
                : TimeOnly.FromTimeSpan(reader.GetTimeSpan(3));

            var endTime = reader.IsDBNull(4)
                ? (TimeOnly?)null
                : TimeOnly.FromTimeSpan(reader.GetTimeSpan(4));

            return Results.Ok(new
            {
                id = reader.GetGuid(0),
                exceptionDate = DateOnly.FromDateTime(reader.GetDateTime(1)),
                isOpen,
                schedule = isOpen
                    ? $"{startTime:HH\\:mm} às {endTime:HH\\:mm}"
                    : "Fechado",
                startTime,
                endTime,
                reason = reader.IsDBNull(5) ? null : reader.GetString(5),
                description = reader.IsDBNull(6) ? null : reader.GetString(6),
                isActive = reader.GetBoolean(7),
                createdAt = reader.GetDateTime(8),
                updatedAt = reader.IsDBNull(9) ? (DateTime?)null : reader.GetDateTime(9)
            });
        })
        .WithTags("Attendance - Exceptions")
        .RequireAuthorization();
        app.MapDelete("/attendance/exceptions/{id:guid}", async (Guid id, IConfiguration config) =>
        {
            var connectionString = config.GetConnectionString("DefaultConnection");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            const string sql = @"
                DELETE FROM business_hour_exceptions
                WHERE id = @id;
            ";

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", id);

            var rowsAffected = await command.ExecuteNonQueryAsync();

            if (rowsAffected == 0)
            {
                return Results.NotFound(new
                {
                    message = "Exceção de horário não encontrada."
                });
            }

            return Results.Ok(new
            {
                id,
                message = "Exceção de horário excluída permanentemente."
            });
        })
        .WithTags("Attendance - Exceptions")
        .RequireAuthorization();      
        app.MapPut("/attendance/exceptions/{id:guid}", async (Guid id, UpdateBusinessHourExceptionRequest request, IConfiguration config, HttpContext httpContext) =>
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var now = TimeOnly.FromDateTime(DateTime.Now);
            var userId = Guid.Parse(
                httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            if (request.ExceptionDate < today)
            {
                return Results.BadRequest(new
                {
                    message = "Não é possível atualizar exceções para datas passadas."
                });
            }

            if (!request.IsOpen)
            {
                return Results.BadRequest(new
                {
                    message = "Para exceções de atendimento, informe um horário inicial e final."
                });
            }

            if (!request.StartTime.HasValue || !request.EndTime.HasValue)
            {
                return Results.BadRequest(new
                {
                    message = "Informe o horário inicial e final da exceção."
                });
            }

            if (request.StartTime >= request.EndTime)
            {
                return Results.BadRequest(new
                {
                    message = "O horário inicial deve ser menor que o horário final."
                });
            }

            if (request.ExceptionDate == today && request.EndTime.Value <= now)
            {
                return Results.BadRequest(new
                {
                    message = "Não é possível atualizar uma exceção cujo horário final já passou."
                });
            }

            var connectionString = config.GetConnectionString("DefaultConnection");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            const string currentSql = @"
                SELECT is_active
                FROM business_hour_exceptions
                WHERE id = @id;
            ";

            await using (var currentCommand = new NpgsqlCommand(currentSql, connection))
            {
                currentCommand.Parameters.AddWithValue("id", id);

                var currentResult = await currentCommand.ExecuteScalarAsync();

                if (currentResult is null)
                {
                    return Results.NotFound(new
                    {
                        message = "Exceção de horário não encontrada."
                    });
                }

                var currentIsActive = (bool)currentResult;

                if (!currentIsActive)
                {
                    return Results.BadRequest(new
                    {
                        message = "Não é possível editar uma exceção inativa. Ative o registro antes de editar."
                    });
                }
            }

            const string duplicateSql = @"
                SELECT COUNT(*)
                FROM business_hour_exceptions
                WHERE exception_date = @exceptionDate
                AND is_active = TRUE
                AND id <> @id;
            ";

            await using (var duplicateCommand = new NpgsqlCommand(duplicateSql, connection))
            {
                duplicateCommand.Parameters.AddWithValue("exceptionDate", request.ExceptionDate.ToDateTime(TimeOnly.MinValue));
                duplicateCommand.Parameters.AddWithValue("id", id);

                var duplicateCount = (long)(await duplicateCommand.ExecuteScalarAsync() ?? 0);

                if (duplicateCount > 0)
                {
                    return Results.Conflict(new
                    {
                        message = "Já existe uma exceção ativa cadastrada para esta data."
                    });
                }
            }

            var dayOfWeek = (int)request.ExceptionDate.DayOfWeek;

            const string businessHourSql = @"
                SELECT is_open, start_time, end_time
                FROM business_hours
                WHERE day_of_week = @dayOfWeek
                AND is_active = TRUE
                LIMIT 1;
            ";

            await using (var businessHourCommand = new NpgsqlCommand(businessHourSql, connection))
            {
                businessHourCommand.Parameters.AddWithValue("dayOfWeek", dayOfWeek);

                await using var businessHourReader = await businessHourCommand.ExecuteReaderAsync();

                if (!await businessHourReader.ReadAsync())
                {
                    return Results.BadRequest(new
                    {
                        message = "Não há horário padrão configurado para o dia informado."
                    });
                }

                var defaultIsOpen = businessHourReader.GetBoolean(0);

                var defaultStartTime = businessHourReader.IsDBNull(1)
                    ? (TimeOnly?)null
                    : TimeOnly.FromTimeSpan(businessHourReader.GetTimeSpan(1));

                var defaultEndTime = businessHourReader.IsDBNull(2)
                    ? (TimeOnly?)null
                    : TimeOnly.FromTimeSpan(businessHourReader.GetTimeSpan(2));

                if (!defaultIsOpen || !defaultStartTime.HasValue || !defaultEndTime.HasValue)
                {
                    return Results.BadRequest(new
                    {
                        message = "Não há atendimento padrão no dia informado."
                    });
                }

                if (request.StartTime.Value < defaultStartTime.Value ||
                    request.EndTime.Value > defaultEndTime.Value)
                {
                    return Results.BadRequest(new
                    {
                        message = $"O horário especial deve estar dentro do horário padrão do dia: {defaultStartTime:HH\\:mm} às {defaultEndTime:HH\\:mm}."
                    });
                }
            }

            const string sql = @"
                UPDATE business_hour_exceptions
                SET
                    exception_date = @exceptionDate,
                    is_open = @isOpen,
                    start_time = @startTime,
                    end_time = @endTime,
                    reason = @reason,
                    description = @description,
                    updated_by_user_id = @updatedByUserId,
                    updated_at = CURRENT_TIMESTAMP
                WHERE id = @id
                AND is_active = TRUE;
            ";

            await using var command = new NpgsqlCommand(sql, connection);

            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("exceptionDate", request.ExceptionDate.ToDateTime(TimeOnly.MinValue));
            command.Parameters.AddWithValue("isOpen", request.IsOpen);
            command.Parameters.AddWithValue("startTime", request.StartTime.Value.ToTimeSpan());
            command.Parameters.AddWithValue("endTime", request.EndTime.Value.ToTimeSpan());
            command.Parameters.AddWithValue("reason", (object?)request.Reason ?? DBNull.Value);
            command.Parameters.AddWithValue("description", (object?)request.Description ?? DBNull.Value);
            command.Parameters.AddWithValue("updatedByUserId", userId);

            var rowsAffected = await command.ExecuteNonQueryAsync();

            if (rowsAffected == 0)
            {
                return Results.NotFound(new
                {
                    message = "Exceção de horário não encontrada ou inativa."
                });
            }

            return Results.Ok(new
            {
                id,
                message = "Exceção de horário atualizada com sucesso."
            });
        })
        .WithTags("Attendance - Exceptions")
        .RequireAuthorization();
        app.MapPatch("/attendance/exceptions/{id:guid}/deactivate", async (Guid id, IConfiguration config, HttpContext httpContext) =>
        {
            var userId = Guid.Parse(
                httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var connectionString = config.GetConnectionString("DefaultConnection");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            const string sql = @"
                UPDATE business_hour_exceptions
                SET
                    is_active = FALSE,
                    updated_by_user_id = @updatedByUserId,
                    updated_at = CURRENT_TIMESTAMP
                WHERE id = @id
                AND is_active = TRUE;
            ";

            await using var command = new NpgsqlCommand(sql, connection);

            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("updatedByUserId", userId);

            var rowsAffected = await command.ExecuteNonQueryAsync();

            if (rowsAffected == 0)
            {
                return Results.NotFound(new
                {
                    message = "Horário especial não encontrado ou já está inativo."
                });
            }

            return Results.Ok(new
            {
                id,
                message = "Horário especial inativado com sucesso."
            });
        })
        .WithTags("Attendance - Exceptions")
        .RequireAuthorization();        
        app.MapPatch("/attendance/exceptions/{id:guid}/activate", async (Guid id, IConfiguration config, HttpContext httpContext) =>
        {
            var userId = Guid.Parse(
                httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var connectionString = config.GetConnectionString("DefaultConnection");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            const string getExceptionSql = @"
                SELECT exception_date, is_active
                FROM business_hour_exceptions
                WHERE id = @id;
            ";

            DateOnly exceptionDate;
            bool isActive;

            await using (var getExceptionCommand = new NpgsqlCommand(getExceptionSql, connection))
            {
                getExceptionCommand.Parameters.AddWithValue("id", id);

                await using var reader = await getExceptionCommand.ExecuteReaderAsync();

                if (!await reader.ReadAsync())
                {
                    return Results.NotFound(new
                    {
                        message = "Horário especial não encontrado."
                    });
                }

                exceptionDate = DateOnly.FromDateTime(reader.GetDateTime(0));
                isActive = reader.GetBoolean(1);
            }

            if (isActive)
            {
                return Results.BadRequest(new
                {
                    message = "Este horário especial já está ativo."
                });
            }

            const string duplicateSql = @"
                SELECT COUNT(*)
                FROM business_hour_exceptions
                WHERE exception_date = @exceptionDate
                AND is_active = TRUE
                AND id <> @id;
            ";

            await using (var duplicateCommand = new NpgsqlCommand(duplicateSql, connection))
            {
                duplicateCommand.Parameters.AddWithValue("exceptionDate", exceptionDate.ToDateTime(TimeOnly.MinValue));
                duplicateCommand.Parameters.AddWithValue("id", id);

                var duplicateCount = (long)(await duplicateCommand.ExecuteScalarAsync() ?? 0);

                if (duplicateCount > 0)
                {
                    return Results.Conflict(new
                    {
                        message = "Não é possível ativar este horário especial porque já existe uma exceção ativa para esta data."
                    });
                }
            }

            const string sql = @"
                UPDATE business_hour_exceptions
                SET
                    is_active = TRUE,
                    updated_by_user_id = @updatedByUserId,
                    updated_at = CURRENT_TIMESTAMP
                WHERE id = @id
                AND is_active = FALSE;
            ";

            await using var command = new NpgsqlCommand(sql, connection);

            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("updatedByUserId", userId);

            var rowsAffected = await command.ExecuteNonQueryAsync();

            if (rowsAffected == 0)
            {
                return Results.NotFound(new
                {
                    message = "Horário especial não encontrado ou já está ativo."
                });
            }

            return Results.Ok(new
            {
                id,
                message = "Horário especial ativado com sucesso."
            });
        })
        .WithTags("Attendance - Exceptions")
        .RequireAuthorization();
        app.MapPut("/attendance/business-hours/{dayOfWeek:int}", async (int dayOfWeek, UpdateBusinessHourRequest request, IConfiguration config, HttpContext httpContext) =>
        {
            var userId = Guid.Parse(
            httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            if (dayOfWeek < 0 || dayOfWeek > 6)
            {
                return Results.BadRequest(new
                {
                    message = "O dia da semana deve estar entre 0 e 6."
                });
            }

            var connectionString = config.GetConnectionString("DefaultConnection");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            const string sql = @"
                UPDATE business_hours
                SET
                    is_open = @isOpen,
                    start_time = @startTime,
                    end_time = @endTime,
                    updated_by_user_id = @updatedByUserId,
                    updated_at = CURRENT_TIMESTAMP
                WHERE day_of_week = @dayOfWeek
                AND is_active = TRUE;
            ";

            await using var command = new NpgsqlCommand(sql, connection);

            command.Parameters.AddWithValue("dayOfWeek", dayOfWeek);
            command.Parameters.AddWithValue("isOpen", request.IsOpen);
            command.Parameters.AddWithValue("startTime", request.StartTime.HasValue ? request.StartTime.Value.ToTimeSpan() : DBNull.Value);
            command.Parameters.AddWithValue("endTime", request.EndTime.HasValue ? request.EndTime.Value.ToTimeSpan() : DBNull.Value);
            command.Parameters.AddWithValue("updatedByUserId", userId);

            var rowsAffected = await command.ExecuteNonQueryAsync();

            if (rowsAffected == 0)
            {
                return Results.NotFound(new
                {
                    message = "Horário padrão não encontrado para o dia informado."
                });
            }

            return Results.Ok(new
            {
                dayOfWeek,
                dayName = GetDayName((short)dayOfWeek),
                message = "Horário padrão atualizado com sucesso."
            });
        })
        .WithTags("Attendance - Business Hours")
        .RequireAuthorization();
        app.MapGet("/attendance/business-hours", async (IConfiguration config) =>
        {
            var connectionString = config.GetConnectionString("DefaultConnection");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            const string sql = @"
                SELECT
                    id,
                    day_of_week,
                    is_open,
                    start_time,
                    end_time,
                    is_active,
                    created_at,
                    updated_at
                FROM business_hours
                WHERE is_active = TRUE
                ORDER BY day_of_week;
            ";

            await using var command = new NpgsqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync();

            var businessHours = new List<object>();

            while (await reader.ReadAsync())
            {
                var isOpen = reader.GetBoolean(2);

                var startTime = reader.IsDBNull(3)
                    ? (TimeOnly?)null
                    : TimeOnly.FromTimeSpan(reader.GetTimeSpan(3));

                var endTime = reader.IsDBNull(4)
                    ? (TimeOnly?)null
                    : TimeOnly.FromTimeSpan(reader.GetTimeSpan(4));

                businessHours.Add(new
                {
                    id = reader.GetGuid(0),
                    dayOfWeek = reader.GetInt16(1),
                    dayName = GetDayName(reader.GetInt16(1)),
                    isOpen,
                    schedule = isOpen
                        ? $"{startTime:HH\\:mm} às {endTime:HH\\:mm}"
                        : "Fechado",
                    startTime,
                    endTime,
                    isActive = reader.GetBoolean(5),
                    createdAt = reader.GetDateTime(6),
                    updatedAt = reader.IsDBNull(7) ? (DateTime?)null : reader.GetDateTime(7)
                });
            }

            return Results.Ok(businessHours);
        })
        .WithTags("Attendance - Business Hours")
        .RequireAuthorization();

        return app;
    }
    public record CreateBusinessHourExceptionRequest(
        DateOnly ExceptionDate,
        bool IsOpen,
        TimeOnly? StartTime,
        TimeOnly? EndTime,
        string? Reason,
        string? Description
    );
    public record UpdateBusinessHourExceptionRequest(
        DateOnly ExceptionDate,
        bool IsOpen,
        TimeOnly? StartTime,
        TimeOnly? EndTime,
        string? Reason,
        string? Description
    );
    public record UpdateBusinessHourRequest(
        bool IsOpen,
        TimeOnly? StartTime,
        TimeOnly? EndTime
    );
    static string GetDayName(short dayOfWeek)
    {
        return dayOfWeek switch
        {
            0 => "Domingo",
            1 => "Segunda-feira",
            2 => "Terça-feira",
            3 => "Quarta-feira",
            4 => "Quinta-feira",
            5 => "Sexta-feira",
            6 => "Sábado",
            _ => "Desconhecido"
        };
    }
}