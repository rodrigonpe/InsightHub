using System.Text.RegularExpressions;

namespace InsightHub.Api.Formatters;

public sealed partial class AltuHtmlMessageFormatter
{
    public string Format(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var formattedHtml = html.Trim();

        formattedHtml = EmptyParagraphRegex().Replace(
            formattedHtml,
            "<br/>");

        return formattedHtml;
    }

    [GeneratedRegex(
        @"<p(?:\s[^>]*)?>\s*(?:&nbsp;|&#160;|<br\s*/?>|\s)*</p>",
        RegexOptions.IgnoreCase)]
    private static partial Regex EmptyParagraphRegex();
}