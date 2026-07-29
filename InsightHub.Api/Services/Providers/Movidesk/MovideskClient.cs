using System.Text.Json;
using InsightHub.Api.Models.Providers;
using InsightHub.Api.Services.Movidesk.Dtos;
using Microsoft.Extensions.Options;

namespace InsightHub.Api.Services.Movidesk;

public class MovideskClient
{
    private readonly HttpClient _httpClient;
    private readonly MovideskOptions _options;

    public MovideskClient(HttpClient httpClient, IOptions<MovideskOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<ProviderTicket?> GetTicketAsync(string ticketId)
    {
        var url =
            $"{_options.BaseUrl}/tickets" +
            $"?token={Uri.EscapeDataString(_options.Token)}" +
            $"&id={Uri.EscapeDataString(ticketId)}";

        var response = await _httpClient.GetAsync(url);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        File.WriteAllText("movidesk-ticket.json", json);
        var dto = JsonSerializer.Deserialize<MovideskTicketDto>(json, new JsonSerializerOptions
        
        {
            PropertyNameCaseInsensitive = true
        });

        if (dto is null)
            return null;

        return new ProviderTicket
        {
            ProviderTicketId = dto.Id ?? ticketId,
            Subject = dto.Subject,
            Status = dto.Status,
            Reason = dto.Justification,
            RequesterName = dto.Clients?.FirstOrDefault()?.BusinessName,
            OwnerName = dto.Owner?.BusinessName,
            OwnerTeam = dto.OwnerTeam,
            OpenedAt = dto.CreatedDate,
            LastInteractionAt = dto.LastActionDate ?? dto.LastUpdate
        };
    }
}