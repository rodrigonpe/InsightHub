using System.Net.Http.Json;
using System.Security.Claims;
using InsightHub.Admin.Components;
using InsightHub.Admin.Models;
using InsightHub.Admin.Services.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddHttpContextAccessor();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
    });

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddHttpClient("InsightHubApi", client =>
{
    client.BaseAddress = new Uri("http://localhost:5283/");
});

builder.Services.AddHttpClient("InsightHubApiAnonymous", client =>
{
    client.BaseAddress = new Uri("http://localhost:5283/");
});

builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IHttpClientFactory>()
      .CreateClient("InsightHubApi"));

builder.Services.AddScoped<BusinessHourService>();
builder.Services.AddScoped<BusinessHourExceptionService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapPost("/login", async (
    [FromForm] LoginRequest login,
    IHttpClientFactory httpClientFactory,
    HttpContext httpContext) =>
{
    var client = httpClientFactory.CreateClient("InsightHubApiAnonymous");

    var response = await client.PostAsJsonAsync("auth/login", login);

    if (!response.IsSuccessStatusCode)
    {
        return Results.Redirect("/login?error=1");
    }

    var result = await response.Content.ReadFromJsonAsync<LoginResponse>();

    if (result is null || string.IsNullOrWhiteSpace(result.Token))
    {
        return Results.Redirect("/login?error=1");
    }

    var claims = new List<Claim>
    {
        new Claim(ClaimTypes.Name, login.Email),
        new Claim(ClaimTypes.Email, login.Email),
        new Claim(ClaimTypes.Role, "Administrator"),
        new Claim("access_token", result.Token)
    };

    var identity = new ClaimsIdentity(
        claims,
        CookieAuthenticationDefaults.AuthenticationScheme);

    var principal = new ClaimsPrincipal(identity);

    await httpContext.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        principal);

    return Results.Redirect("/attendance");
})
.DisableAntiforgery();

app.MapPost("/logout", async (HttpContext httpContext) =>
{
    await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

    return Results.Redirect("/login");
})
.DisableAntiforgery();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();