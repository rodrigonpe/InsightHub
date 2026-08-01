using InsightHub.Api.Services.BotAnnouncements;
using System.Text.Json;
using NpgsqlTypes;
using Npgsql;
using System.Security.Claims;
using InsightHub.Api.Formatters;

public static class BotAnnouncementEndpoints
{
    public static IEndpointRouteBuilder MapBotAnnouncementEndpoints(this IEndpointRouteBuilder app)
    {        
        
        app.MapPost("/bot/announcements", async (CreateBotAnnouncementRequest request, IConfiguration config, ClaimsPrincipal user) =>
        {
            var userIdClaim = user.FindFirst("sub")?.Value
                ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                if (!Guid.TryParse(userIdClaim, out var createdBy))
                {
                    return Results.Unauthorized();
                }

            var fieldValidation = ValidateBotAnnouncementFields(
                request.Title,
                request.Type,
                request.Reason,
                request.MessageHtml,
                "ACTIVE");

            if (fieldValidation is not null)
            {
                return fieldValidation;
            }

            if (!AnnouncementPolicy.TryResolvePriority(
                    request.Type,
                    request.Reason,
                    out var priority,
                    out var priorityError))
            {
                return Results.BadRequest(new
                {
                    message = priorityError
                });
            }

            var now = DateTime.Now;
            var effectiveStartsAt = request.StartsAt ?? now;

            if (request.StartsAt.HasValue &&
                request.StartsAt.Value < now.AddMinutes(-1))
            {
                return Results.BadRequest(new
                {
                    message = "A data e hora de início não podem ser anteriores ao momento atual."
                });
            }

            if (request.ExpiresAt.HasValue &&
                request.ExpiresAt.Value <= now)
            {
                return Results.BadRequest(new
                {
                    message = "A data e hora de expiração devem ser posteriores ao momento atual."
                });
            }

            if (request.ExpiresAt.HasValue &&
                request.ExpiresAt.Value <= effectiveStartsAt)
            {
                return Results.BadRequest(new
                {
                    message = "A data e hora de expiração devem ser posteriores ao início do comunicado."
                });
            }

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
                    effectiveStartsAt;

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
                        source_announcement_id,
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
                        @sourceAnnouncementId,
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

                insertCommand.Parameters.AddWithValue("priority", priority);
                insertCommand.Parameters.AddWithValue("stopBot", request.StopBot);
                insertCommand.Parameters.AddWithValue("messageHtml", request.MessageHtml);

                insertCommand.Parameters.Add("messageText", NpgsqlDbType.Text).Value =
                    string.IsNullOrWhiteSpace(request.MessageText) ? DBNull.Value : request.MessageText;

                insertCommand.Parameters.Add("startsAt", NpgsqlDbType.Timestamp).Value =
                    effectiveStartsAt;

                insertCommand.Parameters.Add("expiresAt", NpgsqlDbType.Timestamp).Value =
                    request.ExpiresAt.HasValue ? request.ExpiresAt.Value : DBNull.Value;

                insertCommand.Parameters.Add("sourceAnnouncementId", NpgsqlDbType.Uuid).Value =
                    request.SourceAnnouncementId.HasValue
                        ? request.SourceAnnouncementId.Value
                        : DBNull.Value;

                insertCommand.Parameters.AddWithValue("createdBy", createdBy);

                await insertCommand.ExecuteNonQueryAsync();

                var newData = JsonSerializer.Serialize(new
                {
                    id,
                    request.Title,
                    request.Type,
                    Status = "ACTIVE",
                    request.Reason,
                    priority,
                    request.StopBot,
                    request.MessageHtml,
                    request.MessageText,
                    StartsAt = effectiveStartsAt,
                    request.ExpiresAt,
                    request.SourceAnnouncementId,
                    CreatedBy = createdBy,
                    CreatedAt = now
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
        .WithTags("Bot - Announcements")
        .RequireAuthorization();
        app.MapGet("/bot/announcements/active", async (IConfiguration config, AltuHtmlMessageFormatter htmlFormatter) =>
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
                    type = (string?)null,
                    reason = (string?)null,
                    stopBot = false,
                    messageHtml = (string?)null,
                    messageText = (string?)null,
                    expiresAt = (DateTime?)null
                });
            }

            var messageHtmlOrdinal = reader.GetOrdinal("message_html");

            var originalMessageHtml = reader.IsDBNull(messageHtmlOrdinal)
                ? null
                : reader.GetString(messageHtmlOrdinal);

            var messageHtmlForAltu = htmlFormatter.Format(originalMessageHtml);

            return Results.Ok(new
            {
                hasAnnouncement = true,

                type = reader.GetString(
                    reader.GetOrdinal("type")),

                reason = reader.IsDBNull(
                    reader.GetOrdinal("reason"))
                        ? null
                        : reader.GetString(
                            reader.GetOrdinal("reason")),

                stopBot = reader.GetBoolean(
                    reader.GetOrdinal("stop_bot")),

                messageHtml = messageHtmlForAltu,

                messageText = reader.IsDBNull(
                    reader.GetOrdinal("message_text"))
                        ? null
                        : reader.GetString(
                            reader.GetOrdinal("message_text")),

                expiresAt = reader.IsDBNull(
                    reader.GetOrdinal("expires_at"))
                        ? (DateTime?)null
                        : reader.GetDateTime(
                            reader.GetOrdinal("expires_at"))
            });
        })
        .WithTags("Bot - Announcements");
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
                    source_announcement_id,
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

                    sourceAnnouncementId = reader.IsDBNull(reader.GetOrdinal("source_announcement_id"))
                        ? (Guid?)null
                        : reader.GetGuid(reader.GetOrdinal("source_announcement_id")),

                    createdAt = reader.GetDateTime(reader.GetOrdinal("created_at"))
                });
            }

            return Results.Ok(announcements);

        })
        .WithTags("Bot - Announcements")
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
                    source_announcement_id,
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

                sourceAnnouncementId = reader.IsDBNull(reader.GetOrdinal("source_announcement_id"))
                    ? (Guid?)null
                    : reader.GetGuid(reader.GetOrdinal("source_announcement_id")),

                createdAt = reader.GetDateTime(reader.GetOrdinal("created_at")),

                updatedAt = reader.IsDBNull(reader.GetOrdinal("updated_at"))
                    ? (DateTime?)null
                    : reader.GetDateTime(reader.GetOrdinal("updated_at")),

                deactivatedAt = reader.IsDBNull(reader.GetOrdinal("deactivated_at"))
                    ? (DateTime?)null
                    : reader.GetDateTime(reader.GetOrdinal("deactivated_at"))
            });

        })
        .WithTags("Bot - Announcements")
        /*.RequireAuthorization()*/;
        app.MapPut("/bot/announcements/{id:guid}", async (Guid id, UpdateBotAnnouncementRequest request, IConfiguration config, ClaimsPrincipal user) =>
        {
            var userIdClaim = user.FindFirst("sub")?.Value
                ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!Guid.TryParse(userIdClaim, out var updatedBy))
            {
                return Results.Unauthorized();
            }

            var fieldValidation = ValidateBotAnnouncementFields(
                request.Title,
                request.Type,
                request.Reason,
                request.MessageHtml,
                request.Status);

            if (fieldValidation is not null)
            {
                return fieldValidation;
            }

            if (!AnnouncementPolicy.TryResolvePriority(
                    request.Type,
                    request.Reason,
                    out var priority,
                    out var priorityError))
            {
                return Results.BadRequest(new
                {
                    message = priorityError
                });
            }

            var connectionString =
                config.GetConnectionString("DefaultConnection");

            await using var connection =
                new NpgsqlConnection(connectionString);

            await connection.OpenAsync();

            await using var transaction =
                await connection.BeginTransactionAsync();

            try
            {
                const string currentSql = """
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
                        source_announcement_id,
                        created_by,
                        created_at,
                        updated_by,
                        updated_at,
                        deactivated_by,
                        deactivated_at
                    FROM bot_announcements
                    WHERE id = @id;
                    """;

                await using var currentCommand =
                    new NpgsqlCommand(currentSql, connection, transaction);

                currentCommand.Parameters.AddWithValue("id", id);

                await using var reader =
                    await currentCommand.ExecuteReaderAsync();

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

                    sourceAnnouncementId =
                        reader.IsDBNull(reader.GetOrdinal("source_announcement_id"))
                            ? (Guid?)null
                            : reader.GetGuid(reader.GetOrdinal("source_announcement_id")),

                    createdBy = reader.GetGuid(reader.GetOrdinal("created_by")),
                    createdAt = reader.GetDateTime(reader.GetOrdinal("created_at")),

                    updatedBy = reader.IsDBNull(reader.GetOrdinal("updated_by"))
                        ? (Guid?)null
                        : reader.GetGuid(reader.GetOrdinal("updated_by")),

                    updatedAt = reader.IsDBNull(reader.GetOrdinal("updated_at"))
                        ? (DateTime?)null
                        : reader.GetDateTime(reader.GetOrdinal("updated_at")),

                    deactivatedBy = reader.IsDBNull(reader.GetOrdinal("deactivated_by"))
                        ? (Guid?)null
                        : reader.GetGuid(reader.GetOrdinal("deactivated_by")),

                    deactivatedAt = reader.IsDBNull(reader.GetOrdinal("deactivated_at"))
                        ? (DateTime?)null
                        : reader.GetDateTime(reader.GetOrdinal("deactivated_at"))
                };

                await reader.CloseAsync();

                var now = DateTime.Now;
                var originalStartsAt = oldDataObject.startsAt;
                var originalExpiresAt = oldDataObject.expiresAt;

                var hasStarted =
                    originalStartsAt.HasValue &&
                    originalStartsAt.Value <= now;

                var hasExpired =
                    originalExpiresAt.HasValue &&
                    originalExpiresAt.Value <= now;

                if (hasExpired)
                {
                    return Results.Conflict(new
                    {
                        message =
                            "Comunicados encerrados não podem ser editados. " +
                            "Utilize a opção Reutilizar para criar um novo comunicado."
                    });
                }

                DateTime effectiveStartsAt;

                if (hasStarted)
                {
                    // O comunicado já começou. O início histórico deve ser preservado.
                    effectiveStartsAt = originalStartsAt!.Value;
                }
                else
                {
                    // O comunicado ainda não começou e pode receber um novo início.
                    effectiveStartsAt = request.StartsAt ?? now;

                    if (request.StartsAt.HasValue &&
                        request.StartsAt.Value < now.AddMinutes(-1))
                    {
                        return Results.BadRequest(new
                        {
                            message =
                                "A nova data e hora de início não podem ser anteriores ao momento atual."
                        });
                    }
                }

                if (request.ExpiresAt.HasValue &&
                    request.ExpiresAt.Value <= now)
                {
                    return Results.BadRequest(new
                    {
                        message =
                            "A data e hora de expiração devem ser posteriores ao momento atual."
                    });
                }

                if (request.ExpiresAt.HasValue &&
                    request.ExpiresAt.Value <= effectiveStartsAt)
                {
                    return Results.BadRequest(new
                    {
                        message =
                            "A data e hora de expiração devem ser posteriores ao início do comunicado."
                    });
                }

                if (request.StopBot &&
                    request.Status == "ACTIVE")
                {
                    const string overlapSql = """
                        SELECT COUNT(*)
                        FROM bot_announcements
                        WHERE id <> @id
                          AND status = 'ACTIVE'
                          AND stop_bot = TRUE
                          AND starts_at <= COALESCE(
                              @expiresAt,
                              'infinity'::timestamp
                          )
                          AND COALESCE(
                              expires_at,
                              'infinity'::timestamp
                          ) >= @startsAt;
                        """;

                    await using var overlapCommand =
                        new NpgsqlCommand(overlapSql, connection, transaction);

                    overlapCommand.Parameters.AddWithValue("id", id);

                    overlapCommand.Parameters
                        .Add("startsAt", NpgsqlDbType.Timestamp)
                        .Value = effectiveStartsAt;

                    overlapCommand.Parameters
                        .Add("expiresAt", NpgsqlDbType.Timestamp)
                        .Value = request.ExpiresAt.HasValue
                            ? request.ExpiresAt.Value
                            : DBNull.Value;

                    var overlappingCount =
                        Convert.ToInt32(
                            await overlapCommand.ExecuteScalarAsync());

                    if (overlappingCount > 0)
                    {
                        return Results.Conflict(new
                        {
                            message =
                                "Já existe outro comunicado ativo que interrompe o atendimento neste período."
                        });
                    }
                }

                const string updateSql = """
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
                            WHEN @status = 'INACTIVE'
                                THEN @updatedBy
                            ELSE NULL
                        END,
                        deactivated_at = CASE
                            WHEN @status = 'INACTIVE'
                                THEN NOW()
                            ELSE NULL
                        END
                    WHERE id = @id;
                    """;

                await using var updateCommand =
                    new NpgsqlCommand(updateSql, connection, transaction);

                updateCommand.Parameters.AddWithValue("id", id);
                updateCommand.Parameters.AddWithValue("title", request.Title.Trim());
                updateCommand.Parameters.AddWithValue("type", request.Type);
                updateCommand.Parameters.AddWithValue("status", request.Status);

                updateCommand.Parameters
                    .Add("reason", NpgsqlDbType.Text)
                    .Value = string.IsNullOrWhiteSpace(request.Reason)
                        ? DBNull.Value
                        : request.Reason;

                updateCommand.Parameters.AddWithValue(
                    "priority",
                    priority);

                updateCommand.Parameters.AddWithValue(
                    "stopBot",
                    request.StopBot);

                updateCommand.Parameters.AddWithValue(
                    "messageHtml",
                    request.MessageHtml);

                updateCommand.Parameters
                    .Add("messageText", NpgsqlDbType.Text)
                    .Value = string.IsNullOrWhiteSpace(request.MessageText)
                        ? DBNull.Value
                        : request.MessageText;

                updateCommand.Parameters
                    .Add("startsAt", NpgsqlDbType.Timestamp)
                    .Value = effectiveStartsAt;

                updateCommand.Parameters
                    .Add("expiresAt", NpgsqlDbType.Timestamp)
                    .Value = request.ExpiresAt.HasValue
                        ? request.ExpiresAt.Value
                        : DBNull.Value;

                updateCommand.Parameters.AddWithValue(
                    "updatedBy",
                    updatedBy);

                var rowsAffected =
                    await updateCommand.ExecuteNonQueryAsync();

                if (rowsAffected == 0)
                {
                    return Results.NotFound(new
                    {
                        message = "Comunicado não encontrado."
                    });
                }

                var newDataObject = new
                {
                    id,
                    title = request.Title.Trim(),
                    type = request.Type,
                    status = request.Status,

                    reason = string.IsNullOrWhiteSpace(request.Reason)
                        ? null
                        : request.Reason,

                    priority = priority,
                    stopBot = request.StopBot,
                    messageHtml = request.MessageHtml,

                    messageText =
                        string.IsNullOrWhiteSpace(request.MessageText)
                            ? null
                            : request.MessageText,

                    startsAt = effectiveStartsAt,
                    expiresAt = request.ExpiresAt,
                    sourceAnnouncementId = oldDataObject.sourceAnnouncementId,
                    createdBy = oldDataObject.createdBy,
                    createdAt = oldDataObject.createdAt,
                    updatedBy,
                    updatedAt = now,

                    deactivatedBy =
                        request.Status == "INACTIVE"
                            ? updatedBy
                            : (Guid?)null,

                    deactivatedAt =
                        request.Status == "INACTIVE"
                            ? now
                            : (DateTime?)null
                };

                const string auditSql = """
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
                    """;

                await using var auditCommand =
                    new NpgsqlCommand(auditSql, connection, transaction);

                auditCommand.Parameters.AddWithValue(
                    "auditId",
                    Guid.NewGuid());

                auditCommand.Parameters.AddWithValue(
                    "announcementId",
                    id);

                auditCommand.Parameters.AddWithValue(
                    "oldData",
                    JsonSerializer.Serialize(oldDataObject));

                auditCommand.Parameters.AddWithValue(
                    "newData",
                    JsonSerializer.Serialize(newDataObject));

                auditCommand.Parameters.AddWithValue(
                    "performedBy",
                    updatedBy);

                await auditCommand.ExecuteNonQueryAsync();

                await transaction.CommitAsync();

                return Results.Ok(new
                {
                    id,
                    startsAt = effectiveStartsAt,
                    expiresAt = request.ExpiresAt,
                    message = "Comunicado atualizado com sucesso."
                });
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        })
        .WithTags("Bot - Announcements")
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
        .WithTags("Bot - Announcements")
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

                if (expiresAt.HasValue &&
                    expiresAt.Value <= DateTime.Now)
                {
                    return Results.Conflict(new
                    {
                        message =
                            "Comunicados encerrados não podem ser reativados. " +
                            "Utilize a opção Reutilizar para criar um novo comunicado."
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
        .WithTags("Bot - Announcements")
        .RequireAuthorization();
        app.MapPatch("/bot/announcements/{id:guid}/extend", async (Guid id, ExtendBotAnnouncementRequest request, IConfiguration config, ClaimsPrincipal user) =>
        {
            var userIdClaim = user.FindFirst("sub")?.Value
                ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!Guid.TryParse(userIdClaim, out var performedBy))
                return Results.Unauthorized();

            var now = DateTime.Now;

            if (request.ExpiresAt <= now)
            {
                return Results.BadRequest(new
                {
                    message = "A nova data de expiração deve ser posterior ao momento atual."
                });
            }

            var connectionString =
                config.GetConnectionString("DefaultConnection");

            await using var connection =
                new NpgsqlConnection(connectionString);

            await connection.OpenAsync();

            await using var transaction =
                await connection.BeginTransactionAsync();

            try
            {
                const string currentSql = """
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
                    """;

                await using var currentCommand =
                    new NpgsqlCommand(currentSql, connection, transaction);

                currentCommand.Parameters.AddWithValue("id", id);

                await using var reader =
                    await currentCommand.ExecuteReaderAsync();

                if (!await reader.ReadAsync())
                {
                    return Results.NotFound(new
                    {
                        message = "Comunicado não encontrado."
                    });
                }

                var status =
                    reader.GetString(reader.GetOrdinal("status"));

                var currentExpiresAt =
                    reader.IsDBNull(reader.GetOrdinal("expires_at"))
                        ? (DateTime?)null
                        : reader.GetDateTime(reader.GetOrdinal("expires_at"));

                var oldDataObject = new
                {
                    id = reader.GetGuid(reader.GetOrdinal("id")),
                    title = reader.GetString(reader.GetOrdinal("title")),
                    type = reader.GetString(reader.GetOrdinal("type")),
                    status,
                    reason = reader.IsDBNull(reader.GetOrdinal("reason"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("reason")),
                    priority = reader.GetInt32(reader.GetOrdinal("priority")),
                    stopBot = reader.GetBoolean(reader.GetOrdinal("stop_bot")),
                    messageHtml =
                        reader.GetString(reader.GetOrdinal("message_html")),
                    messageText =
                        reader.IsDBNull(reader.GetOrdinal("message_text"))
                            ? null
                            : reader.GetString(reader.GetOrdinal("message_text")),
                    startsAt =
                        reader.IsDBNull(reader.GetOrdinal("starts_at"))
                            ? (DateTime?)null
                            : reader.GetDateTime(reader.GetOrdinal("starts_at")),
                    expiresAt = currentExpiresAt,
                    createdBy =
                        reader.GetGuid(reader.GetOrdinal("created_by")),
                    createdAt =
                        reader.GetDateTime(reader.GetOrdinal("created_at")),
                    updatedBy =
                        reader.IsDBNull(reader.GetOrdinal("updated_by"))
                            ? (Guid?)null
                            : reader.GetGuid(reader.GetOrdinal("updated_by")),
                    updatedAt =
                        reader.IsDBNull(reader.GetOrdinal("updated_at"))
                            ? (DateTime?)null
                            : reader.GetDateTime(reader.GetOrdinal("updated_at")),
                    deactivatedBy =
                        reader.IsDBNull(reader.GetOrdinal("deactivated_by"))
                            ? (Guid?)null
                            : reader.GetGuid(reader.GetOrdinal("deactivated_by")),
                    deactivatedAt =
                        reader.IsDBNull(reader.GetOrdinal("deactivated_at"))
                            ? (DateTime?)null
                            : reader.GetDateTime(reader.GetOrdinal("deactivated_at"))
                };

                await reader.CloseAsync();

                if (status != "ACTIVE")
                {
                    return Results.BadRequest(new
                    {
                        message = "Somente comunicados ativos podem ter a vigência estendida."
                    });
                }

                if (!currentExpiresAt.HasValue)
                {
                    return Results.BadRequest(new
                    {
                        message = "Este comunicado não possui data de expiração."
                    });
                }

                if (currentExpiresAt.Value <= now)
                {
                    return Results.Conflict(new
                    {
                        message = "Este comunicado já expirou. Duplique e reagende o comunicado para utilizá-lo novamente."
                    });
                }

                if (request.ExpiresAt <= currentExpiresAt.Value)
                {
                    return Results.BadRequest(new
                    {
                        message = "A nova expiração deve ser posterior à expiração atual."
                    });
                }

                const string updateSql = """
                    UPDATE bot_announcements
                    SET
                        expires_at = @expiresAt,
                        updated_by = @performedBy,
                        updated_at = NOW()
                    WHERE id = @id;
                    """;

                await using var updateCommand =
                    new NpgsqlCommand(updateSql, connection, transaction);

                updateCommand.Parameters.AddWithValue("id", id);
                updateCommand.Parameters.AddWithValue(
                    "expiresAt",
                    request.ExpiresAt);
                updateCommand.Parameters.AddWithValue(
                    "performedBy",
                    performedBy);

                await updateCommand.ExecuteNonQueryAsync();

                var newDataObject = new
                {
                    oldDataObject.id,
                    oldDataObject.title,
                    oldDataObject.type,
                    oldDataObject.status,
                    oldDataObject.reason,
                    oldDataObject.priority,
                    oldDataObject.stopBot,
                    oldDataObject.messageHtml,
                    oldDataObject.messageText,
                    oldDataObject.startsAt,
                    expiresAt = request.ExpiresAt,
                    oldDataObject.createdBy,
                    oldDataObject.createdAt,
                    updatedBy = performedBy,
                    updatedAt = DateTime.Now,
                    oldDataObject.deactivatedBy,
                    oldDataObject.deactivatedAt
                };

                const string auditSql = """
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
                        'EXTENDED',
                        @oldData::jsonb,
                        @newData::jsonb,
                        @performedBy,
                        NOW()
                    );
                    """;

                await using var auditCommand =
                    new NpgsqlCommand(auditSql, connection, transaction);

                auditCommand.Parameters.AddWithValue(
                    "auditId",
                    Guid.NewGuid());

                auditCommand.Parameters.AddWithValue(
                    "announcementId",
                    id);

                auditCommand.Parameters.AddWithValue(
                    "oldData",
                    JsonSerializer.Serialize(oldDataObject));

                auditCommand.Parameters.AddWithValue(
                    "newData",
                    JsonSerializer.Serialize(newDataObject));

                auditCommand.Parameters.AddWithValue(
                    "performedBy",
                    performedBy);

                await auditCommand.ExecuteNonQueryAsync();

                await transaction.CommitAsync();

                return Results.Ok(new
                {
                    id,
                    expiresAt = request.ExpiresAt,
                    message = "Vigência estendida com sucesso."
                });
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        })
        .WithTags("Bot - Announcements")
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
        .WithTags("Bot - Announcements")
        .RequireAuthorization(); 
        
        return app;
    }
    public record CreateBotAnnouncementRequest(
        string Title,
        string Type,
        string? Reason,
        bool StopBot,
        string MessageHtml,
        string? MessageText,
        DateTime? StartsAt,
        DateTime? ExpiresAt,
        Guid? SourceAnnouncementId
    );
    public record UpdateBotAnnouncementRequest(
        string Title,
        string Type,
        string? Reason,
        bool StopBot,
        string MessageHtml,
        string? MessageText,
        DateTime? StartsAt,
        DateTime? ExpiresAt,
        string Status
    );
    static IResult? ValidateBotAnnouncementFields(string? title, string? type, string? reason, string? messageHtml, string? status)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Results.BadRequest(new
            {
                message = "Informe o título do comunicado."
            });
        }

        if (title.Trim().Length > 150)
        {
            return Results.BadRequest(new
            {
                message = "O título deve ter no máximo 150 caracteres."
            });
        }

        var validTypes = new[]
        {
            "INFO",
            "WARNING",
            "MAINTENANCE",
            "PAUSE",
            "CAMPAIGN"
        };

        if (string.IsNullOrWhiteSpace(type) ||
            !validTypes.Contains(type))
        {
            return Results.BadRequest(new
            {
                message = "Tipo de comunicado inválido."
            });
        }

        var validStatuses = new[]
        {
            "ACTIVE",
            "INACTIVE"
        };

        if (string.IsNullOrWhiteSpace(status) ||
            !validStatuses.Contains(status))
        {
            return Results.BadRequest(new
            {
                message = "Status do comunicado inválido."
            });
        }

        var validReasons = new[]
        {
            "MAINTENANCE",
            "POWER_OUTAGE",
            "INSTABILITY",
            "HOLIDAY",
            "EMERGENCY",
            "OTHER"
        };

        if (!string.IsNullOrWhiteSpace(reason) &&
            !validReasons.Contains(reason))
        {
            return Results.BadRequest(new
            {
                message = "Motivo do comunicado inválido."
            });
        }

        if ((type == "MAINTENANCE" ||
             type == "PAUSE") &&
            string.IsNullOrWhiteSpace(reason))
        {
            return Results.BadRequest(new
            {
                message =
                    "Informe o motivo para comunicados de manutenção ou pausa."
            });
        }


        if (string.IsNullOrWhiteSpace(messageHtml))
        {
            return Results.BadRequest(new
            {
                message = "Informe a mensagem do comunicado."
            });
        }

        return null;
    }
    public record ExtendBotAnnouncementRequest(DateTime ExpiresAt
    );
}