namespace InsightHub.Api.BotAnnouncements.Models;

public interface IHtmlMessageFormatter
{
    string Format(string? html);
}