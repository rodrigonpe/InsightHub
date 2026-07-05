using System.Net.Http.Headers;
using System.Net.Http.Json;
using InsightHub.Admin.Models;
using Microsoft.AspNetCore.Http;

namespace InsightHub.Admin.Services.Api;

public class BusinessHourService
{
    private readonly HttpClient _http;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public BusinessHourService(HttpClient http, IHttpContextAccessor httpContextAccessor)
    {
        _http = http;
        _httpContextAccessor = httpContextAccessor;
    }

    private void AddAuthorizationHeader()
    {
        var token = _httpContextAccessor.HttpContext?
            .User
            .FindFirst("access_token")?
            .Value;

        if (!string.IsNullOrWhiteSpace(token))
        {
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
        }
    }

    public async Task<List<BusinessHourResponse>> GetBusinessHoursAsync()
    {
        AddAuthorizationHeader();

        return await _http.GetFromJsonAsync<List<BusinessHourResponse>>(
            "attendance/business-hours") ?? [];
    }

    public async Task<bool> UpdateBusinessHourAsync(BusinessHourResponse businessHour)
    {
        AddAuthorizationHeader();

        var request = new
        {
            isOpen = businessHour.IsOpen,
            startTime = businessHour.StartTime,
            endTime = businessHour.EndTime
        };

        var response = await _http.PutAsJsonAsync(
            $"attendance/business-hours/{businessHour.DayOfWeek}",
            request);

        return response.IsSuccessStatusCode;
    }
}