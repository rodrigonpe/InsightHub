using InsightHub.Services;
using InsightHub.Api.Services.Movidesk;
using InsightHub.Api.Models.Requests;
using InsightHub.Api.Validators;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Microsoft.OpenApi.Models;
internal class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddEndpointsApiExplorer();
        
        builder.Services.Configure<MovideskOptions>(
        builder.Configuration.GetSection("Movidesk"));

        builder.Services.AddHttpClient<MovideskClient>();

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
        app.MapPost("/users/generate-password-hash", (GeneratePasswordHashRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.Password))
            {
                return Results.BadRequest(new
                {
                    message = "A senha é obrigatória."
                });
            }

            var hash = BCrypt.Net.BCrypt.HashPassword(request.Password);

            return Results.Ok(new
            {
                passwordHash = hash
            });
        })
        //.RequireAuthorization()
        .WithName("GeneratePasswordHash");
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
        app.MapPost("/bot/announcements", async (CreateBotAnnouncementRequest request, IConfiguration config, ClaimsPrincipal user) =>
        {
            var userIdClaim = user.FindFirst("sub")?.Value
                ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                if (!Guid.TryParse(userIdClaim, out var createdBy))
                {
                    return Results.Unauthorized();
                }

            if (string.IsNullOrWhiteSpace(request.Title))
                return Results.BadRequest(new { message = "Informe o título do comunicado." });

            if (request.Title.Length > 150)
                return Results.BadRequest(new { message = "O título deve ter no máximo 150 caracteres." });

            if (string.IsNullOrWhiteSpace(request.Type))
                return Results.BadRequest(new { message = "Informe o tipo do comunicado." });

            var validTypes = new[] { "INFO", "WARNING", "MAINTENANCE", "PAUSE", "CAMPAIGN" };
            if (!validTypes.Contains(request.Type))
                return Results.BadRequest(new { message = "Tipo de comunicado inválido." });

            var validReasons = new[] { "MAINTENANCE", "POWER_OUTAGE", "INSTABILITY", "HOLIDAY", "EMERGENCY", "OTHER" };
            if (!string.IsNullOrWhiteSpace(request.Reason) && !validReasons.Contains(request.Reason))
                return Results.BadRequest(new { message = "Motivo do comunicado inválido." });

            if ((request.Type == "MAINTENANCE" || request.Type == "PAUSE") && string.IsNullOrWhiteSpace(request.Reason))
                return Results.BadRequest(new { message = "Informe o motivo para comunicados de manutenção ou pausa." });

            if (request.Priority < 0)
                return Results.BadRequest(new { message = "A prioridade não pode ser negativa." });

            if (string.IsNullOrWhiteSpace(request.MessageHtml))
                return Results.BadRequest(new { message = "Informe a mensagem HTML do comunicado." });

            if (request.StartsAt.HasValue && request.ExpiresAt.HasValue && request.StartsAt >= request.ExpiresAt)
                return Results.BadRequest(new { message = "A data de início deve ser menor que a data de expiração." });

            var connectionString = config.GetConnectionString("DefaultConnection");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            if (request.StopBot)
            {
                const string overlapSql = @"
                    SELECT COUNT(*)
                    FROM bot_announcements
                    WHERE status = 'ACTIVE'
                    AND stop_bot = TRUE
                    AND COALESCE(starts_at, '-infinity'::timestamp) <= COALESCE(@expiresAt, 'infinity'::timestamp)
                    AND COALESCE(expires_at, 'infinity'::timestamp) >= COALESCE(@startsAt, '-infinity'::timestamp);
                ";

                await using var overlapCommand = new NpgsqlCommand(overlapSql, connection);

                overlapCommand.Parameters.Add("startsAt", NpgsqlDbType.Timestamp).Value =
                    request.StartsAt.HasValue ? request.StartsAt.Value : DBNull.Value;

                overlapCommand.Parameters.Add("expiresAt", NpgsqlDbType.Timestamp).Value =
                    request.ExpiresAt.HasValue ? request.ExpiresAt.Value : DBNull.Value;

                var overlappingCount = Convert.ToInt32(await overlapCommand.ExecuteScalarAsync());

                if (overlappingCount > 0)
                {
                    return Results.Conflict(new
                    {
                        message = "Já existe um comunicado ativo que interrompe o atendimento neste período."
                    });
                }
            }

            var id = Guid.NewGuid();

            await using var transaction = await connection.BeginTransactionAsync();

            try
            {
                const string insertSql = @"
                    INSERT INTO bot_announcements (
                        id,
                        title,
                        type,
                        status,
                        reason,
                        priority,
                        stop_bot,
                        message_html,
                        message_text,
                        starts_at,
                        expires_at,
                        created_by,
                        created_at
                    )
                    VALUES (
                        @id,
                        @title,
                        @type::announcement_type,
                        'ACTIVE',
                        @reason::announcement_reason,
                        @priority,
                        @stopBot,
                        @messageHtml,
                        @messageText,
                        @startsAt,
                        @expiresAt,
                        @createdBy,
                        NOW()
                    );
                ";

                await using var insertCommand = new NpgsqlCommand(insertSql, connection, transaction);

                insertCommand.Parameters.AddWithValue("id", id);
                insertCommand.Parameters.AddWithValue("title", request.Title);
                insertCommand.Parameters.AddWithValue("type", request.Type);

                insertCommand.Parameters.Add("reason", NpgsqlDbType.Text).Value =
                    string.IsNullOrWhiteSpace(request.Reason) ? DBNull.Value : request.Reason;

                insertCommand.Parameters.AddWithValue("priority", request.Priority);
                insertCommand.Parameters.AddWithValue("stopBot", request.StopBot);
                insertCommand.Parameters.AddWithValue("messageHtml", request.MessageHtml);

                insertCommand.Parameters.Add("messageText", NpgsqlDbType.Text).Value =
                    string.IsNullOrWhiteSpace(request.MessageText) ? DBNull.Value : request.MessageText;

                insertCommand.Parameters.Add("startsAt", NpgsqlDbType.Timestamp).Value =
                    request.StartsAt.HasValue ? request.StartsAt.Value : DBNull.Value;

                insertCommand.Parameters.Add("expiresAt", NpgsqlDbType.Timestamp).Value =
                    request.ExpiresAt.HasValue ? request.ExpiresAt.Value : DBNull.Value;

                insertCommand.Parameters.AddWithValue("createdBy", createdBy);

                await insertCommand.ExecuteNonQueryAsync();

                var newData = JsonSerializer.Serialize(new
                {
                    id,
                    request.Title,
                    request.Type,
                    Status = "ACTIVE",
                    request.Reason,
                    request.Priority,
                    request.StopBot,
                    request.MessageHtml,
                    request.MessageText,
                    request.StartsAt,
                    request.ExpiresAt,
                    CreatedBy = createdBy,
                    CreatedAt = DateTime.UtcNow
                });

                const string auditSql = @"
                    INSERT INTO bot_announcements_audit (
                        id,
                        announcement_id,
                        action,
                        old_data,
                        new_data,
                        performed_by,
                        performed_at
                    )
                    VALUES (
                        @auditId,
                        @announcementId,
                        'CREATED',
                        NULL,
                        @newData::jsonb,
                        @performedBy,
                        NOW()
                    );
                ";

                await using var auditCommand = new NpgsqlCommand(auditSql, connection, transaction);

                auditCommand.Parameters.AddWithValue("auditId", Guid.NewGuid());
                auditCommand.Parameters.AddWithValue("announcementId", id);
                auditCommand.Parameters.AddWithValue("newData", newData);
                auditCommand.Parameters.AddWithValue("performedBy", createdBy);

                await auditCommand.ExecuteNonQueryAsync();

                await transaction.CommitAsync();

                return Results.Created($"/bot/announcements/{id}", new
                {
                    id,
                    message = "Comunicado criado com sucesso."
                });
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        })
        .RequireAuthorization();
        app.MapGet("/bot/announcements/active", async (IConfiguration config) =>
        {
            var connectionString = config.GetConnectionString("DefaultConnection");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            const string sql = @"
                SELECT
                    type,
                    reason,
                    stop_bot,
                    message_html,
                    message_text,
                    expires_at
                FROM bot_announcements
                WHERE status = 'ACTIVE'
                AND (starts_at IS NULL OR starts_at <= NOW())
                AND (expires_at IS NULL OR expires_at >= NOW())
                ORDER BY
                    stop_bot DESC,
                    priority DESC,
                    created_at DESC
                LIMIT 1;
            ";

            await using var command = new NpgsqlCommand(sql, connection);

            await using var reader = await command.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                return Results.Ok(new
                {
                    hasAnnouncement = false,
                    stopBot = false,
                    messageHtml = (string?)null,
                    messageText = (string?)null
                });
            }

            return Results.Ok(new
            {
                hasAnnouncement = true,
                type = reader.GetString(reader.GetOrdinal("type")),
                reason = reader.IsDBNull(reader.GetOrdinal("reason"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("reason")),
                stopBot = reader.GetBoolean(reader.GetOrdinal("stop_bot")),
                messageHtml = reader.GetString(reader.GetOrdinal("message_html")),
                messageText = reader.IsDBNull(reader.GetOrdinal("message_text"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("message_text")),
                expiresAt = reader.IsDBNull(reader.GetOrdinal("expires_at"))
                    ? (DateTime?)null
                    : reader.GetDateTime(reader.GetOrdinal("expires_at"))
            });
        });
        app.MapGet("/bot/announcements", async (IConfiguration config) =>
        {
            var connectionString = config.GetConnectionString("DefaultConnection");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            const string sql = @"
                SELECT
                    id,
                    title,
                    type,
                    status,
                    reason,
                    priority,
                    stop_bot,
                    starts_at,
                    expires_at,
                    created_at
                FROM bot_announcements
                ORDER BY
                    created_at DESC;
            ";

            await using var command = new NpgsqlCommand(sql, connection);

            await using var reader = await command.ExecuteReaderAsync();

            var announcements = new List<object>();

            while (await reader.ReadAsync())
            {
                announcements.Add(new
                {
                    id = reader.GetGuid(reader.GetOrdinal("id")),
                    title = reader.GetString(reader.GetOrdinal("title")),
                    type = reader.GetString(reader.GetOrdinal("type")),
                    status = reader.GetString(reader.GetOrdinal("status")),

                    reason = reader.IsDBNull(reader.GetOrdinal("reason"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("reason")),

                    priority = reader.GetInt32(reader.GetOrdinal("priority")),

                    stopBot = reader.GetBoolean(reader.GetOrdinal("stop_bot")),

                    startsAt = reader.IsDBNull(reader.GetOrdinal("starts_at"))
                        ? (DateTime?)null
                        : reader.GetDateTime(reader.GetOrdinal("starts_at")),

                    expiresAt = reader.IsDBNull(reader.GetOrdinal("expires_at"))
                        ? (DateTime?)null
                        : reader.GetDateTime(reader.GetOrdinal("expires_at")),

                    createdAt = reader.GetDateTime(reader.GetOrdinal("created_at"))
                });
            }

            return Results.Ok(announcements);

        })
        /*.RequireAuthorization()*/;
        app.MapGet("/bot/announcements/{id:guid}", async (Guid id, IConfiguration config) =>
        {
            var connectionString = config.GetConnectionString("DefaultConnection");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            const string sql = @"
                SELECT
                    id,
                    title,
                    type,
                    status,
                    reason,
                    priority,
                    stop_bot,
                    message_html,
                    message_text,
                    starts_at,
                    expires_at,
                    created_at,
                    updated_at,
                    deactivated_at
                FROM bot_announcements
                WHERE id = @id;
            ";

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", id);

            await using var reader = await command.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                return Results.NotFound(new
                {
                    message = "Comunicado não encontrado."
                });
            }

            return Results.Ok(new
            {
                id = reader.GetGuid(reader.GetOrdinal("id")),
                title = reader.GetString(reader.GetOrdinal("title")),
                type = reader.GetString(reader.GetOrdinal("type")),
                status = reader.GetString(reader.GetOrdinal("status")),

                reason = reader.IsDBNull(reader.GetOrdinal("reason"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("reason")),

                priority = reader.GetInt32(reader.GetOrdinal("priority")),

                stopBot = reader.GetBoolean(reader.GetOrdinal("stop_bot")),

                messageHtml = reader.GetString(reader.GetOrdinal("message_html")),

                messageText = reader.IsDBNull(reader.GetOrdinal("message_text"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("message_text")),

                startsAt = reader.IsDBNull(reader.GetOrdinal("starts_at"))
                    ? (DateTime?)null
                    : reader.GetDateTime(reader.GetOrdinal("starts_at")),

                expiresAt = reader.IsDBNull(reader.GetOrdinal("expires_at"))
                    ? (DateTime?)null
                    : reader.GetDateTime(reader.GetOrdinal("expires_at")),

                createdAt = reader.GetDateTime(reader.GetOrdinal("created_at")),

                updatedAt = reader.IsDBNull(reader.GetOrdinal("updated_at"))
                    ? (DateTime?)null
                    : reader.GetDateTime(reader.GetOrdinal("updated_at")),

                deactivatedAt = reader.IsDBNull(reader.GetOrdinal("deactivated_at"))
                    ? (DateTime?)null
                    : reader.GetDateTime(reader.GetOrdinal("deactivated_at"))
            });

        })
        /*.RequireAuthorization()*/;
        app.MapPut("/bot/announcements/{id:guid}", async (Guid id, UpdateBotAnnouncementRequest request, IConfiguration config, ClaimsPrincipal user) =>
        {
            var userIdClaim = user.FindFirst("sub")?.Value
                ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!Guid.TryParse(userIdClaim, out var updatedBy))
                return Results.Unauthorized();

            var validationResult = ValidateBotAnnouncement(
                request.Title,
                request.Type,
                request.Reason,
                request.Priority,
                request.MessageHtml,
                request.StartsAt,
                request.ExpiresAt,
                request.Status
            );

            if (validationResult is not null)
                return validationResult;

            var connectionString = config.GetConnectionString("DefaultConnection");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            if (request.StopBot && request.Status == "ACTIVE")
            {
                const string overlapSql = @"
                    SELECT COUNT(*)
                    FROM bot_announcements
                    WHERE id <> @id
                    AND status = 'ACTIVE'
                    AND stop_bot = TRUE
                    AND COALESCE(starts_at, '-infinity'::timestamp) <= COALESCE(@expiresAt, 'infinity'::timestamp)
                    AND COALESCE(expires_at, 'infinity'::timestamp) >= COALESCE(@startsAt, '-infinity'::timestamp);
                ";

                await using var overlapCommand = new NpgsqlCommand(overlapSql, connection);

                overlapCommand.Parameters.AddWithValue("id", id);

                overlapCommand.Parameters.Add("startsAt", NpgsqlDbType.Timestamp).Value =
                    request.StartsAt.HasValue ? request.StartsAt.Value : DBNull.Value;

                overlapCommand.Parameters.Add("expiresAt", NpgsqlDbType.Timestamp).Value =
                    request.ExpiresAt.HasValue ? request.ExpiresAt.Value : DBNull.Value;

                var overlappingCount = Convert.ToInt32(await overlapCommand.ExecuteScalarAsync());

                if (overlappingCount > 0)
                {
                    return Results.Conflict(new
                    {
                        message = "Já existe outro comunicado ativo que interrompe o atendimento neste período."
                    });
                }
            }

            await using var transaction = await connection.BeginTransactionAsync();

            try
            {
                const string currentSql = @"
                    SELECT
                        id,
                        title,
                        type,
                        status,
                        reason,
                        priority,
                        stop_bot,
                        message_html,
                        message_text,
                        starts_at,
                        expires_at,
                        created_by,
                        created_at,
                        updated_by,
                        updated_at,
                        deactivated_by,
                        deactivated_at
                    FROM bot_announcements
                    WHERE id = @id;
                ";

                await using var currentCommand = new NpgsqlCommand(currentSql, connection, transaction);
                currentCommand.Parameters.AddWithValue("id", id);

                await using var reader = await currentCommand.ExecuteReaderAsync();

                if (!await reader.ReadAsync())
                {
                    return Results.NotFound(new
                    {
                        message = "Comunicado não encontrado."
                    });
                }

                var oldDataObject = new
                {
                    id = reader.GetGuid(reader.GetOrdinal("id")),
                    title = reader.GetString(reader.GetOrdinal("title")),
                    type = reader.GetString(reader.GetOrdinal("type")),
                    status = reader.GetString(reader.GetOrdinal("status")),
                    reason = reader.IsDBNull(reader.GetOrdinal("reason")) ? null : reader.GetString(reader.GetOrdinal("reason")),
                    priority = reader.GetInt32(reader.GetOrdinal("priority")),
                    stopBot = reader.GetBoolean(reader.GetOrdinal("stop_bot")),
                    messageHtml = reader.GetString(reader.GetOrdinal("message_html")),
                    messageText = reader.IsDBNull(reader.GetOrdinal("message_text")) ? null : reader.GetString(reader.GetOrdinal("message_text")),
                    startsAt = reader.IsDBNull(reader.GetOrdinal("starts_at")) ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("starts_at")),
                    expiresAt = reader.IsDBNull(reader.GetOrdinal("expires_at")) ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("expires_at")),
                    createdBy = reader.GetGuid(reader.GetOrdinal("created_by")),
                    createdAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
                    updatedBy = reader.IsDBNull(reader.GetOrdinal("updated_by")) ? (Guid?)null : reader.GetGuid(reader.GetOrdinal("updated_by")),
                    updatedAt = reader.IsDBNull(reader.GetOrdinal("updated_at")) ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("updated_at")),
                    deactivatedBy = reader.IsDBNull(reader.GetOrdinal("deactivated_by")) ? (Guid?)null : reader.GetGuid(reader.GetOrdinal("deactivated_by")),
                    deactivatedAt = reader.IsDBNull(reader.GetOrdinal("deactivated_at")) ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("deactivated_at"))
                };

                await reader.CloseAsync();

                const string updateSql = @"
                    UPDATE bot_announcements
                    SET
                        title = @title,
                        type = @type::announcement_type,
                        status = @status::announcement_status,
                        reason = @reason::announcement_reason,
                        priority = @priority,
                        stop_bot = @stopBot,
                        message_html = @messageHtml,
                        message_text = @messageText,
                        starts_at = @startsAt,
                        expires_at = @expiresAt,
                        updated_by = @updatedBy,
                        updated_at = NOW(),
                        deactivated_by = CASE
                            WHEN @status = 'INACTIVE' THEN @updatedBy
                            ELSE deactivated_by
                        END,
                        deactivated_at = CASE
                            WHEN @status = 'INACTIVE' THEN NOW()
                            ELSE deactivated_at
                        END
                    WHERE id = @id;
                ";

                await using var updateCommand = new NpgsqlCommand(updateSql, connection, transaction);

                updateCommand.Parameters.AddWithValue("id", id);
                updateCommand.Parameters.AddWithValue("title", request.Title);
                updateCommand.Parameters.AddWithValue("type", request.Type);
                updateCommand.Parameters.AddWithValue("status", request.Status);

                updateCommand.Parameters.Add("reason", NpgsqlDbType.Text).Value =
                    string.IsNullOrWhiteSpace(request.Reason) ? DBNull.Value : request.Reason;

                updateCommand.Parameters.AddWithValue("priority", request.Priority);
                updateCommand.Parameters.AddWithValue("stopBot", request.StopBot);
                updateCommand.Parameters.AddWithValue("messageHtml", request.MessageHtml);

                updateCommand.Parameters.Add("messageText", NpgsqlDbType.Text).Value =
                    string.IsNullOrWhiteSpace(request.MessageText) ? DBNull.Value : request.MessageText;

                updateCommand.Parameters.Add("startsAt", NpgsqlDbType.Timestamp).Value =
                    request.StartsAt.HasValue ? request.StartsAt.Value : DBNull.Value;

                updateCommand.Parameters.Add("expiresAt", NpgsqlDbType.Timestamp).Value =
                    request.ExpiresAt.HasValue ? request.ExpiresAt.Value : DBNull.Value;

                updateCommand.Parameters.AddWithValue("updatedBy", updatedBy);

                await updateCommand.ExecuteNonQueryAsync();

                var newDataObject = new
                {
                    id,
                    request.Title,
                    request.Type,
                    request.Status,
                    request.Reason,
                    request.Priority,
                    request.StopBot,
                    request.MessageHtml,
                    request.MessageText,
                    request.StartsAt,
                    request.ExpiresAt,
                    UpdatedBy = updatedBy,
                    UpdatedAt = DateTime.UtcNow
                };

                const string auditSql = @"
                    INSERT INTO bot_announcements_audit (
                        id,
                        announcement_id,
                        action,
                        old_data,
                        new_data,
                        performed_by,
                        performed_at
                    )
                    VALUES (
                        @auditId,
                        @announcementId,
                        'UPDATED',
                        @oldData::jsonb,
                        @newData::jsonb,
                        @performedBy,
                        NOW()
                    );
                ";

                await using var auditCommand = new NpgsqlCommand(auditSql, connection, transaction);

                auditCommand.Parameters.AddWithValue("auditId", Guid.NewGuid());
                auditCommand.Parameters.AddWithValue("announcementId", id);
                auditCommand.Parameters.AddWithValue("oldData", JsonSerializer.Serialize(oldDataObject));
                auditCommand.Parameters.AddWithValue("newData", JsonSerializer.Serialize(newDataObject));
                auditCommand.Parameters.AddWithValue("performedBy", updatedBy);

                await auditCommand.ExecuteNonQueryAsync();

                await transaction.CommitAsync();

                return Results.Ok(new
                {
                    id,
                    message = "Comunicado atualizado com sucesso."
                });
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

        })
        .RequireAuthorization();
        app.MapPatch("/bot/announcements/{id:guid}/deactivate", async (Guid id, IConfiguration config, ClaimsPrincipal user) =>
        {
            var userIdClaim = user.FindFirst("sub")?.Value
                ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!Guid.TryParse(userIdClaim, out var performedBy))
                return Results.Unauthorized();

            var connectionString = config.GetConnectionString("DefaultConnection");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            await using var transaction = await connection.BeginTransactionAsync();

            try
            {
                const string currentSql = @"
                    SELECT
                        id,
                        title,
                        type,
                        status,
                        reason,
                        priority,
                        stop_bot,
                        message_html,
                        message_text,
                        starts_at,
                        expires_at,
                        created_by,
                        created_at,
                        updated_by,
                        updated_at,
                        deactivated_by,
                        deactivated_at
                    FROM bot_announcements
                    WHERE id = @id;
                ";

                await using var currentCommand = new NpgsqlCommand(currentSql, connection, transaction);
                currentCommand.Parameters.AddWithValue("id", id);

                await using var reader = await currentCommand.ExecuteReaderAsync();

                if (!await reader.ReadAsync())
                {
                    return Results.NotFound(new
                    {
                        message = "Comunicado não encontrado."
                    });
                }

                var oldStatus = reader.GetString(reader.GetOrdinal("status"));

                var oldDataObject = new
                {
                    id = reader.GetGuid(reader.GetOrdinal("id")),
                    title = reader.GetString(reader.GetOrdinal("title")),
                    type = reader.GetString(reader.GetOrdinal("type")),
                    status = oldStatus,
                    reason = reader.IsDBNull(reader.GetOrdinal("reason")) ? null : reader.GetString(reader.GetOrdinal("reason")),
                    priority = reader.GetInt32(reader.GetOrdinal("priority")),
                    stopBot = reader.GetBoolean(reader.GetOrdinal("stop_bot")),
                    messageHtml = reader.GetString(reader.GetOrdinal("message_html")),
                    messageText = reader.IsDBNull(reader.GetOrdinal("message_text")) ? null : reader.GetString(reader.GetOrdinal("message_text")),
                    startsAt = reader.IsDBNull(reader.GetOrdinal("starts_at")) ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("starts_at")),
                    expiresAt = reader.IsDBNull(reader.GetOrdinal("expires_at")) ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("expires_at")),
                    createdBy = reader.GetGuid(reader.GetOrdinal("created_by")),
                    createdAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
                    updatedBy = reader.IsDBNull(reader.GetOrdinal("updated_by")) ? (Guid?)null : reader.GetGuid(reader.GetOrdinal("updated_by")),
                    updatedAt = reader.IsDBNull(reader.GetOrdinal("updated_at")) ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("updated_at")),
                    deactivatedBy = reader.IsDBNull(reader.GetOrdinal("deactivated_by")) ? (Guid?)null : reader.GetGuid(reader.GetOrdinal("deactivated_by")),
                    deactivatedAt = reader.IsDBNull(reader.GetOrdinal("deactivated_at")) ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("deactivated_at"))
                };

                await reader.CloseAsync();

                if (oldStatus == "INACTIVE")
                {
                    return Results.BadRequest(new
                    {
                        message = "Este comunicado já está inativo."
                    });
                }

                const string updateSql = @"
                    UPDATE bot_announcements
                    SET
                        status = 'INACTIVE',
                        updated_by = @performedBy,
                        updated_at = NOW(),
                        deactivated_by = @performedBy,
                        deactivated_at = NOW()
                    WHERE id = @id;
                ";

                await using var updateCommand = new NpgsqlCommand(updateSql, connection, transaction);
                updateCommand.Parameters.AddWithValue("id", id);
                updateCommand.Parameters.AddWithValue("performedBy", performedBy);

                await updateCommand.ExecuteNonQueryAsync();

                var newDataObject = new
                {
                    oldDataObject.id,
                    oldDataObject.title,
                    oldDataObject.type,
                    status = "INACTIVE",
                    oldDataObject.reason,
                    oldDataObject.priority,
                    oldDataObject.stopBot,
                    oldDataObject.messageHtml,
                    oldDataObject.messageText,
                    oldDataObject.startsAt,
                    oldDataObject.expiresAt,
                    oldDataObject.createdBy,
                    oldDataObject.createdAt,
                    updatedBy = performedBy,
                    updatedAt = DateTime.UtcNow,
                    deactivatedBy = performedBy,
                    deactivatedAt = DateTime.UtcNow
                };

                const string auditSql = @"
                    INSERT INTO bot_announcements_audit (
                        id,
                        announcement_id,
                        action,
                        old_data,
                        new_data,
                        performed_by,
                        performed_at
                    )
                    VALUES (
                        @auditId,
                        @announcementId,
                        'DEACTIVATED',
                        @oldData::jsonb,
                        @newData::jsonb,
                        @performedBy,
                        NOW()
                    );
                ";

                await using var auditCommand = new NpgsqlCommand(auditSql, connection, transaction);

                auditCommand.Parameters.AddWithValue("auditId", Guid.NewGuid());
                auditCommand.Parameters.AddWithValue("announcementId", id);
                auditCommand.Parameters.AddWithValue("oldData", JsonSerializer.Serialize(oldDataObject));
                auditCommand.Parameters.AddWithValue("newData", JsonSerializer.Serialize(newDataObject));
                auditCommand.Parameters.AddWithValue("performedBy", performedBy);

                await auditCommand.ExecuteNonQueryAsync();

                await transaction.CommitAsync();

                return Results.Ok(new
                {
                    id,
                    message = "Comunicado desativado com sucesso."
                });
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

        })
        .RequireAuthorization();
        app.MapPatch("/bot/announcements/{id:guid}/activate", async (Guid id, IConfiguration config, ClaimsPrincipal user) =>
        {
            var userIdClaim = user.FindFirst("sub")?.Value
                ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!Guid.TryParse(userIdClaim, out var performedBy))
                return Results.Unauthorized();

            var connectionString = config.GetConnectionString("DefaultConnection");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            await using var transaction = await connection.BeginTransactionAsync();

            try
            {
                const string currentSql = @"
                    SELECT
                        id,
                        title,
                        type,
                        status,
                        reason,
                        priority,
                        stop_bot,
                        message_html,
                        message_text,
                        starts_at,
                        expires_at,
                        created_by,
                        created_at,
                        updated_by,
                        updated_at,
                        deactivated_by,
                        deactivated_at
                    FROM bot_announcements
                    WHERE id = @id;
                ";

                await using var currentCommand = new NpgsqlCommand(currentSql, connection, transaction);
                currentCommand.Parameters.AddWithValue("id", id);

                await using var reader = await currentCommand.ExecuteReaderAsync();

                if (!await reader.ReadAsync())
                {
                    return Results.NotFound(new
                    {
                        message = "Comunicado não encontrado."
                    });
                }

                var oldStatus = reader.GetString(reader.GetOrdinal("status"));
                var stopBot = reader.GetBoolean(reader.GetOrdinal("stop_bot"));

                var startsAt = reader.IsDBNull(reader.GetOrdinal("starts_at"))
                    ? (DateTime?)null
                    : reader.GetDateTime(reader.GetOrdinal("starts_at"));

                var expiresAt = reader.IsDBNull(reader.GetOrdinal("expires_at"))
                    ? (DateTime?)null
                    : reader.GetDateTime(reader.GetOrdinal("expires_at"));

                var oldDataObject = new
                {
                    id = reader.GetGuid(reader.GetOrdinal("id")),
                    title = reader.GetString(reader.GetOrdinal("title")),
                    type = reader.GetString(reader.GetOrdinal("type")),
                    status = oldStatus,
                    reason = reader.IsDBNull(reader.GetOrdinal("reason")) ? null : reader.GetString(reader.GetOrdinal("reason")),
                    priority = reader.GetInt32(reader.GetOrdinal("priority")),
                    stopBot,
                    messageHtml = reader.GetString(reader.GetOrdinal("message_html")),
                    messageText = reader.IsDBNull(reader.GetOrdinal("message_text")) ? null : reader.GetString(reader.GetOrdinal("message_text")),
                    startsAt,
                    expiresAt,
                    createdBy = reader.GetGuid(reader.GetOrdinal("created_by")),
                    createdAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
                    updatedBy = reader.IsDBNull(reader.GetOrdinal("updated_by")) ? (Guid?)null : reader.GetGuid(reader.GetOrdinal("updated_by")),
                    updatedAt = reader.IsDBNull(reader.GetOrdinal("updated_at")) ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("updated_at")),
                    deactivatedBy = reader.IsDBNull(reader.GetOrdinal("deactivated_by")) ? (Guid?)null : reader.GetGuid(reader.GetOrdinal("deactivated_by")),
                    deactivatedAt = reader.IsDBNull(reader.GetOrdinal("deactivated_at")) ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("deactivated_at"))
                };

                await reader.CloseAsync();

                if (oldStatus == "ACTIVE")
                {
                    return Results.BadRequest(new
                    {
                        message = "Este comunicado já está ativo."
                    });
                }

                if (stopBot)
                {
                    const string overlapSql = @"
                        SELECT COUNT(*)
                        FROM bot_announcements
                        WHERE id <> @id
                        AND status = 'ACTIVE'
                        AND stop_bot = TRUE
                        AND COALESCE(starts_at, '-infinity'::timestamp) <= COALESCE(@expiresAt, 'infinity'::timestamp)
                        AND COALESCE(expires_at, 'infinity'::timestamp) >= COALESCE(@startsAt, '-infinity'::timestamp);
                    ";

                    await using var overlapCommand = new NpgsqlCommand(overlapSql, connection, transaction);

                    overlapCommand.Parameters.AddWithValue("id", id);

                    overlapCommand.Parameters.Add("startsAt", NpgsqlDbType.Timestamp).Value =
                        startsAt.HasValue ? startsAt.Value : DBNull.Value;

                    overlapCommand.Parameters.Add("expiresAt", NpgsqlDbType.Timestamp).Value =
                        expiresAt.HasValue ? expiresAt.Value : DBNull.Value;

                    var overlappingCount = Convert.ToInt32(await overlapCommand.ExecuteScalarAsync());

                    if (overlappingCount > 0)
                    {
                        return Results.Conflict(new
                        {
                            message = "Já existe outro comunicado ativo que interrompe o atendimento neste período."
                        });
                    }
                }

                const string updateSql = @"
                    UPDATE bot_announcements
                    SET
                        status = 'ACTIVE',
                        updated_by = @performedBy,
                        updated_at = NOW(),
                        deactivated_by = NULL,
                        deactivated_at = NULL
                    WHERE id = @id;
                ";

                await using var updateCommand = new NpgsqlCommand(updateSql, connection, transaction);
                updateCommand.Parameters.AddWithValue("id", id);
                updateCommand.Parameters.AddWithValue("performedBy", performedBy);

                await updateCommand.ExecuteNonQueryAsync();

                var newDataObject = new
                {
                    oldDataObject.id,
                    oldDataObject.title,
                    oldDataObject.type,
                    status = "ACTIVE",
                    oldDataObject.reason,
                    oldDataObject.priority,
                    oldDataObject.stopBot,
                    oldDataObject.messageHtml,
                    oldDataObject.messageText,
                    oldDataObject.startsAt,
                    oldDataObject.expiresAt,
                    oldDataObject.createdBy,
                    oldDataObject.createdAt,
                    updatedBy = performedBy,
                    updatedAt = DateTime.UtcNow,
                    deactivatedBy = (Guid?)null,
                    deactivatedAt = (DateTime?)null
                };

                const string auditSql = @"
                    INSERT INTO bot_announcements_audit (
                        id,
                        announcement_id,
                        action,
                        old_data,
                        new_data,
                        performed_by,
                        performed_at
                    )
                    VALUES (
                        @auditId,
                        @announcementId,
                        'ACTIVATED',
                        @oldData::jsonb,
                        @newData::jsonb,
                        @performedBy,
                        NOW()
                    );
                ";

                await using var auditCommand = new NpgsqlCommand(auditSql, connection, transaction);

                auditCommand.Parameters.AddWithValue("auditId", Guid.NewGuid());
                auditCommand.Parameters.AddWithValue("announcementId", id);
                auditCommand.Parameters.AddWithValue("oldData", JsonSerializer.Serialize(oldDataObject));
                auditCommand.Parameters.AddWithValue("newData", JsonSerializer.Serialize(newDataObject));
                auditCommand.Parameters.AddWithValue("performedBy", performedBy);

                await auditCommand.ExecuteNonQueryAsync();

                await transaction.CommitAsync();

                return Results.Ok(new
                {
                    id,
                    message = "Comunicado ativado com sucesso."
                });
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

        })
        .RequireAuthorization();
        app.MapGet("/bot/announcements/{id:guid}/audit", async (Guid id, IConfiguration config) =>
        {
            var connectionString = config.GetConnectionString("DefaultConnection");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            const string existsSql = @"
                SELECT COUNT(*)
                FROM bot_announcements
                WHERE id = @id;
            ";

            await using var existsCommand = new NpgsqlCommand(existsSql, connection);
            existsCommand.Parameters.AddWithValue("id", id);

            var exists = Convert.ToInt32(await existsCommand.ExecuteScalarAsync()) > 0;

            if (!exists)
            {
                return Results.NotFound(new
                {
                    message = "Comunicado não encontrado."
                });
            }

            const string sql = @"
                SELECT
                    a.id,
                    a.action,
                    a.old_data,
                    a.new_data,
                    a.performed_by,
                    u.name AS performed_by_name,
                    a.performed_at,
                    a.ip_address,
                    a.user_agent
                FROM bot_announcements_audit a
                LEFT JOIN users u ON u.id = a.performed_by
                WHERE a.announcement_id = @announcementId
                ORDER BY a.performed_at DESC;
            ";

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("announcementId", id);

            await using var reader = await command.ExecuteReaderAsync();

            var auditLogs = new List<object>();

            while (await reader.ReadAsync())
            {
                auditLogs.Add(new
                {
                    id = reader.GetGuid(reader.GetOrdinal("id")),
                    action = reader.GetString(reader.GetOrdinal("action")),

                    oldData = reader.IsDBNull(reader.GetOrdinal("old_data"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("old_data")),

                    newData = reader.IsDBNull(reader.GetOrdinal("new_data"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("new_data")),

                    performedBy = reader.GetGuid(reader.GetOrdinal("performed_by")),

                    performedByName = reader.IsDBNull(reader.GetOrdinal("performed_by_name"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("performed_by_name")),

                    performedAt = reader.GetDateTime(reader.GetOrdinal("performed_at")),

                    ipAddress = reader.IsDBNull(reader.GetOrdinal("ip_address"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("ip_address")),

                    userAgent = reader.IsDBNull(reader.GetOrdinal("user_agent"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("user_agent"))
                });
            }

            return Results.Ok(auditLogs);

        })
        .RequireAuthorization();
        app.MapPost("/followup-tickets/{ticketId}/sync", async (string ticketId, IConfiguration config, MovideskClient movideskClient) =>
        {
            var providerTicket = await movideskClient.GetTicketAsync(ticketId);

            if (providerTicket is null)
            {
                return Results.NotFound(new
                {
                    message = "Ticket não encontrado no Movidesk."
                });
            }

            var connectionString = config.GetConnectionString("DefaultConnection");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            const string providerSql = @"
                SELECT id
                FROM integration_providers
                WHERE code = 'MOVIDESK'
                AND is_active = TRUE
                LIMIT 1;
            ";

            await using var providerCommand = new NpgsqlCommand(providerSql, connection);
            var providerIdResult = await providerCommand.ExecuteScalarAsync();

            if (providerIdResult is null)
            {
                return Results.BadRequest(new
                {
                    message = "Provedor MOVIDESK não encontrado ou inativo."
                });
            }

            var providerId = (Guid)providerIdResult;

            const string upsertSql = @"
                INSERT INTO followup_tickets (
                    id,
                    provider_id,
                    provider_ticket_id,
                    subject,
                    provider_status,
                    provider_reason,
                    requester_name,
                    owner_name,
                    owner_team,
                    opened_at,
                    last_interaction_at,
                    followup_status,
                    created_at,
                    updated_at
                )
                VALUES (
                    gen_random_uuid(),
                    @providerId,
                    @providerTicketId,
                    @subject,
                    @providerStatus,
                    @providerReason,
                    @requesterName,
                    @ownerName,
                    @ownerTeam,
                    @openedAt,
                    @lastInteractionAt,
                    'MONITORING',
                    NOW(),
                    NOW()
                )
                ON CONFLICT (provider_id, provider_ticket_id)
                DO UPDATE SET
                    subject = EXCLUDED.subject,
                    provider_status = EXCLUDED.provider_status,
                    provider_reason = EXCLUDED.provider_reason,
                    requester_name = EXCLUDED.requester_name,
                    owner_name = EXCLUDED.owner_name,
                    owner_team = EXCLUDED.owner_team,
                    opened_at = EXCLUDED.opened_at,
                    last_interaction_at = EXCLUDED.last_interaction_at,
                    updated_at = NOW()
                RETURNING id;
            ";

            await using var upsertCommand = new NpgsqlCommand(upsertSql, connection);

            upsertCommand.Parameters.AddWithValue("providerId", providerId);
            upsertCommand.Parameters.AddWithValue("providerTicketId", providerTicket.ProviderTicketId);
            upsertCommand.Parameters.AddWithValue("subject", (object?)providerTicket.Subject ?? DBNull.Value);
            upsertCommand.Parameters.AddWithValue("providerStatus", (object?)providerTicket.Status ?? DBNull.Value);
            upsertCommand.Parameters.AddWithValue("providerReason", (object?)providerTicket.Reason ?? DBNull.Value);
            upsertCommand.Parameters.AddWithValue("requesterName", (object?)providerTicket.RequesterName ?? DBNull.Value);
            upsertCommand.Parameters.AddWithValue("ownerName", (object?)providerTicket.OwnerName ?? DBNull.Value);
            upsertCommand.Parameters.AddWithValue("ownerTeam", (object?)providerTicket.OwnerTeam ?? DBNull.Value);
            upsertCommand.Parameters.AddWithValue("openedAt", (object?)providerTicket.OpenedAt ?? DBNull.Value);
            upsertCommand.Parameters.AddWithValue("lastInteractionAt", (object?)providerTicket.LastInteractionAt ?? DBNull.Value);

            var followupTicketId = await upsertCommand.ExecuteScalarAsync();

            const string eventSql = @"
                INSERT INTO followup_ticket_events (
                    id,
                    followup_ticket_id,
                    event_type,
                    description,
                    created_at
                )
                VALUES (
                    gen_random_uuid(),
                    @followupTicketId,
                    'SYNC',
                    @description,
                    NOW()
                );
            ";

            await using var eventCommand = new NpgsqlCommand(eventSql, connection);

            eventCommand.Parameters.AddWithValue("followupTicketId", (Guid)followupTicketId!);
            eventCommand.Parameters.AddWithValue(
                "description",
                $"Ticket {providerTicket.ProviderTicketId} sincronizado com o Movidesk."
            );

            await eventCommand.ExecuteNonQueryAsync();

            return Results.Ok(new
            {
                message = "Ticket sincronizado com sucesso.",
                provider = "MOVIDESK",
                providerTicketId = providerTicket.ProviderTicketId,
                subject = providerTicket.Subject,
                status = providerTicket.Status,
                reason = providerTicket.Reason,
                followupStatus = "MONITORING"
            });
        });
        app.MapGet("/followup-tickets/{ticketId}", async (string ticketId, IConfiguration config) =>
        {
            var connectionString = config.GetConnectionString("DefaultConnection");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            const string sql = @"
                SELECT
                    p.code AS provider,
                    t.provider_ticket_id,
                    t.subject,
                    t.provider_status,
                    t.provider_reason,
                    t.requester_name,
                    t.owner_name,
                    t.owner_team,
                    t.opened_at,
                    t.last_interaction_at,
                    t.business_hours_elapsed,
                    t.next_followup_at,
                    t.last_followup_sent_at,
                    t.followup_status
                FROM followup_tickets t
                INNER JOIN integration_providers p ON p.id = t.provider_id
                WHERE t.provider_ticket_id = @ticketId
                AND p.code = 'MOVIDESK'
                LIMIT 1;
            ";

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("ticketId", ticketId);

            await using var reader = await command.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                return Results.NotFound(new
                {
                    message = "Ticket não encontrado no monitoramento de follow-up."
                });
            }

            return Results.Ok(new
            {
                provider = reader["provider"],
                providerTicketId = reader["provider_ticket_id"],
                subject = reader["subject"] == DBNull.Value ? null : reader["subject"],
                status = reader["provider_status"] == DBNull.Value ? null : reader["provider_status"],
                reason = reader["provider_reason"] == DBNull.Value ? null : reader["provider_reason"],
                requesterName = reader["requester_name"] == DBNull.Value ? null : reader["requester_name"],
                ownerName = reader["owner_name"] == DBNull.Value ? null : reader["owner_name"],
                ownerTeam = reader["owner_team"] == DBNull.Value ? null : reader["owner_team"],
                openedAt = reader["opened_at"] == DBNull.Value ? null : reader["opened_at"],
                lastInteractionAt = reader["last_interaction_at"] == DBNull.Value ? null : reader["last_interaction_at"],
                businessHoursElapsed = reader["business_hours_elapsed"],
                nextFollowupAt = reader["next_followup_at"] == DBNull.Value ? null : reader["next_followup_at"],
                lastFollowupSentAt = reader["last_followup_sent_at"] == DBNull.Value ? null : reader["last_followup_sent_at"],
                followupStatus = reader["followup_status"]
            });
        });
        app.MapGet("/followup-tickets", async (IConfiguration config) =>
        {
            var connectionString = config.GetConnectionString("DefaultConnection");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            const string sql = @"
                SELECT
                    p.code AS provider,
                    t.provider_ticket_id,
                    t.subject,
                    t.provider_status,
                    t.provider_reason,
                    t.requester_name,
                    t.owner_name,
                    t.owner_team,
                    t.opened_at,
                    t.last_interaction_at,
                    t.business_hours_elapsed,
                    t.next_followup_at,
                    t.last_followup_sent_at,
                    t.followup_status,
                    t.created_at,
                    t.updated_at
                FROM followup_tickets t
                INNER JOIN integration_providers p ON p.id = t.provider_id
                WHERE p.code = 'MOVIDESK'
                ORDER BY t.updated_at DESC;
            ";

            await using var command = new NpgsqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync();

            var tickets = new List<object>();

            while (await reader.ReadAsync())
            {
                tickets.Add(new
                {
                    provider = reader["provider"],
                    providerTicketId = reader["provider_ticket_id"],
                    subject = reader["subject"] == DBNull.Value ? null : reader["subject"],
                    status = reader["provider_status"] == DBNull.Value ? null : reader["provider_status"],
                    reason = reader["provider_reason"] == DBNull.Value ? null : reader["provider_reason"],
                    requesterName = reader["requester_name"] == DBNull.Value ? null : reader["requester_name"],
                    ownerName = reader["owner_name"] == DBNull.Value ? null : reader["owner_name"],
                    ownerTeam = reader["owner_team"] == DBNull.Value ? null : reader["owner_team"],
                    openedAt = reader["opened_at"] == DBNull.Value ? null : reader["opened_at"],
                    lastInteractionAt = reader["last_interaction_at"] == DBNull.Value ? null : reader["last_interaction_at"],
                    businessHoursElapsed = reader["business_hours_elapsed"],
                    nextFollowupAt = reader["next_followup_at"] == DBNull.Value ? null : reader["next_followup_at"],
                    lastFollowupSentAt = reader["last_followup_sent_at"] == DBNull.Value ? null : reader["last_followup_sent_at"],
                    followupStatus = reader["followup_status"],
                    createdAt = reader["created_at"],
                    updatedAt = reader["updated_at"]
                });
            }

            return Results.Ok(tickets);
        });
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
    public record CreateBotAnnouncementRequest(
        string Title,
        string Type,
        string? Reason,
        int Priority,
        bool StopBot,
        string MessageHtml,
        string? MessageText,
        DateTime? StartsAt,
        DateTime? ExpiresAt
    );
    public record UpdateBotAnnouncementRequest(
        string Title,
        string Type,
        string? Reason,
        int Priority,
        bool StopBot,
        string MessageHtml,
        string? MessageText,
        DateTime? StartsAt,
        DateTime? ExpiresAt,
        string Status
    );
    static IResult? ValidateBotAnnouncement(
        string title,
        string type,
        string? reason,
        int priority,
        string messageHtml,
        DateTime? startsAt,
        DateTime? expiresAt,
        string? status = null)
    {
        if (string.IsNullOrWhiteSpace(title))
            return Results.BadRequest(new { message = "Informe o título do comunicado." });

        if (title.Length > 150)
            return Results.BadRequest(new { message = "O título deve ter no máximo 150 caracteres." });

        var validTypes = new[] { "INFO", "WARNING", "MAINTENANCE", "PAUSE", "CAMPAIGN" };
        if (string.IsNullOrWhiteSpace(type) || !validTypes.Contains(type))
            return Results.BadRequest(new { message = "Tipo de comunicado inválido." });

        var validReasons = new[] { "MAINTENANCE", "POWER_OUTAGE", "INSTABILITY", "HOLIDAY", "EMERGENCY", "OTHER" };
        if (!string.IsNullOrWhiteSpace(reason) && !validReasons.Contains(reason))
            return Results.BadRequest(new { message = "Motivo do comunicado inválido." });

        if ((type == "MAINTENANCE" || type == "PAUSE") && string.IsNullOrWhiteSpace(reason))
            return Results.BadRequest(new { message = "Informe o motivo para comunicados de manutenção ou pausa." });

        if (priority < 0)
            return Results.BadRequest(new { message = "A prioridade não pode ser negativa." });

        if (string.IsNullOrWhiteSpace(messageHtml))
            return Results.BadRequest(new { message = "Informe a mensagem HTML do comunicado." });

        if (startsAt.HasValue && expiresAt.HasValue && startsAt >= expiresAt)
            return Results.BadRequest(new { message = "A data de início deve ser menor que a data de expiração." });

        if (status is not null)
        {
            var validStatuses = new[] { "ACTIVE", "INACTIVE", "EXPIRED" };
            if (string.IsNullOrWhiteSpace(status) || !validStatuses.Contains(status))
                return Results.BadRequest(new { message = "Status do comunicado inválido." });
        }

        return null;
    }
}