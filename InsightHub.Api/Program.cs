using InsightHub.Services;
using InsightHub.Api.Validators;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.OpenApi.Models;
using Npgsql;
internal class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddEndpointsApiExplorer();

        builder.Services.AddSwaggerGen(options =>
        {
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "Bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Informe apenas o token JWT."
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });
        });

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = builder.Configuration["Jwt:Issuer"],

                    ValidateAudience = true,
                    ValidAudience = builder.Configuration["Jwt:Audience"],

                    ValidateLifetime = true,

                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
                };
            });

        builder.Services.AddAuthorization();

        var app = builder.Build();

        app.UseAuthentication();
        app.UseAuthorization();
 
        app.UseSwagger();
        app.UseSwaggerUI();
  
        app.MapPost("/auth/login", async (LoginRequest request, IConfiguration config) =>
        {
            var connectionString = config.GetConnectionString("DefaultConnection");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            const string sql = @"
                SELECT
                    id,
                    name,
                    email,
                    password_hash,
                    role
                FROM users
                WHERE email = @email
                AND is_active = TRUE
                LIMIT 1;
            ";

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("email", request.Email);

            await using var reader = await command.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                return Results.Unauthorized();
            }

            var userId = reader.GetGuid(0);
            var name = reader.GetString(1);
            var email = reader.GetString(2);
            var passwordHash = reader.GetString(3);
            var role = reader.GetString(4);

            var passwordIsValid = BCrypt.Net.BCrypt.Verify(request.Password, passwordHash);

            if (!passwordIsValid)
            {
                return Results.Unauthorized();
            }

            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(config["Jwt:Key"]!));

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, email),
                new Claim(ClaimTypes.Email, email),
                new Claim(ClaimTypes.Role, role),
                new Claim(ClaimTypes.Name, name)
            };

            var credentials = new SigningCredentials(
                key,
                SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: config["Jwt:Issuer"],
                audience: config["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddHours(1),
                signingCredentials: credentials);

            var jwt = new JwtSecurityTokenHandler()
                .WriteToken(token);

            return Results.Ok(new
            {
                token = jwt
            });
        });
        app.MapGet("/auth/me", (ClaimsPrincipal user) =>
        {
            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var name = user.FindFirst(ClaimTypes.Name)?.Value;
            var email = user.FindFirst(ClaimTypes.Email)?.Value;
            var role = user.FindFirst(ClaimTypes.Role)?.Value;

            return Results.Ok(new
            {
                id = userId,
                name,
                email,
                role
            });
        })
        .RequireAuthorization();
        app.MapGet("/", () =>
        {
            return Results.Redirect("/swagger");
        });
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
        app.MapGet("/calendar/holidays", async (int? year, IConfiguration config) =>
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
        app.MapGet("/calendar/holidays/{id:guid}", async (Guid id, IConfiguration config) =>
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
        });
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
            });
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
        .RequireAuthorization();
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
        .RequireAuthorization();
        app.MapDelete("/attendance/exceptions/{id:guid}", async (Guid id, Guid updatedByUserId, IConfiguration config) =>
        {
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
            command.Parameters.AddWithValue("updatedByUserId", updatedByUserId);

            var rowsAffected = await command.ExecuteNonQueryAsync();

            if (rowsAffected == 0)
            {
                return Results.NotFound(new
                {
                    message = "Exceção de horário não encontrada ou já inativa."
                });
            }

            return Results.Ok(new
            {
                id,
                message = "Exceção de horário inativada com sucesso."
            });
        });      
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
        .RequireAuthorization();
        app.MapPut("/attendance/exceptions/{id:guid}/deactivate", async (Guid id, Guid updatedByUserId, IConfiguration config, HttpContext httpContext) =>
        {
            var connectionString = config.GetConnectionString("DefaultConnection");
            var userId = Guid.Parse(
                httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

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
        .RequireAuthorization();
        app.MapPut("/attendance/exceptions/{id:guid}/activate", async (Guid id, Guid updatedByUserId, IConfiguration config, HttpContext httpContext) =>
        {
            var connectionString = config.GetConnectionString("DefaultConnection");
            var userId = Guid.Parse(
                httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

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
        .RequireAuthorization();
        /*app.MapGet("/calendar/today", () => new
        {
            date = "2026-06-26",
            DayOfWeek = "Sunday",
        }); */
        app.Run();       
    }
    public record LoginRequest(
        string Email,
        string Password
    );
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