using System.Net.Http.Headers;
using System.Net.Http.Json;
using InsightHub.Admin.Models;
using Microsoft.AspNetCore.Http;

namespace InsightHub.Admin.Services.Api;

public class BusinessHourExceptionService
{
    private readonly HttpClient _http;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public BusinessHourExceptionService(HttpClient http, IHttpContextAccessor httpContextAccessor)
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

    public async Task<List<BusinessHourExceptionResponse>> GetExceptionsAsync(bool includeInactive)
    {
        AddAuthorizationHeader();

        var url = includeInactive
            ? "attendance/exceptions?includeInactive=true"
            : "attendance/exceptions";

        return await _http.GetFromJsonAsync<List<BusinessHourExceptionResponse>>(url) ?? [];
    }

    public async Task<(bool Success, string? Error)> CreateAsync(CreateBusinessHourExceptionRequest request)
    {
        AddAuthorizationHeader();

        var response = await _http.PostAsJsonAsync("attendance/exceptions", request);

        if (response.IsSuccessStatusCode)
            return (true, null);

        return (false, await response.Content.ReadAsStringAsync());
    }

    public async Task<(bool Success, string? Error)> UpdateAsync(BusinessHourExceptionResponse exception)
    {
        AddAuthorizationHeader();

    var request = new UpdateBusinessHourExceptionRequest
    {
        ExceptionDate = exception.ExceptionDate,
        IsOpen = exception.IsOpen,
        StartTime = exception.StartTime,
        EndTime = exception.EndTime,
        Reason = exception.Reason,
        Description = exception.Description
    };

        var response = await _http.PatchAsJsonAsync(
            $"attendance/exceptions/{exception.Id}",
            request);

        if (response.IsSuccessStatusCode)
            return (true, null);

        return (false, await response.Content.ReadAsStringAsync());
    }

    public async Task<(bool Success, string? Error)> ChangeStatusAsync(Guid id, bool activate)
    {
        AddAuthorizationHeader();

        var action = activate ? "activate" : "deactivate";

        var response = await _http.PatchAsync(
            $"attendance/exceptions/{id}/{action}",
            null);

        if (response.IsSuccessStatusCode)
            return (true, null);

        return (false, await response.Content.ReadAsStringAsync());
    }
}