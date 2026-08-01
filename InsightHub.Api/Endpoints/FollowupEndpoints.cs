using InsightHub.Api.Services.Providers.Movidesk;
using Npgsql;
public static class FollowupEndpoints
{
    public static IEndpointRouteBuilder MapFollowupEndpoints(this IEndpointRouteBuilder app)
    {

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
        })
        .WithTags("Followup Tickets");
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
        })
        .WithTags("Followup Tickets");
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
        })
        .WithTags("Followup Tickets");

        return app;
    }
}