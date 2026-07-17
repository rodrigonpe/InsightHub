namespace InsightHub.Admin.Services.BotAnnouncements;

public sealed record AnnouncementReasonOption(
    string Value,
    string Label,
    int Priority,
    string PriorityLabel,
    string CssClass);

public static class AnnouncementUiPolicy
{
    public static IReadOnlyList<AnnouncementReasonOption> GetReasons(string? type)
    {
        return type?.Trim().ToUpperInvariant() switch
        {
            "INFO" =>
            [
                Option("HOLIDAY", "Feriado", 50),
                Option("MAINTENANCE", "Manutenção", 50),
                Option("OTHER", "Outro", 25)
            ],

            "WARNING" =>
            [
                Option("EMERGENCY", "Emergência", 100),
                Option("INSTABILITY", "Instabilidade", 75),
                Option("POWER_OUTAGE", "Falta de energia", 75),
                Option("MAINTENANCE", "Manutenção", 50),
                Option("OTHER", "Outro", 50)
            ],

            "MAINTENANCE" =>
            [
                Option("EMERGENCY", "Emergência", 100),
                Option("MAINTENANCE", "Manutenção programada", 75),
                Option("INSTABILITY", "Instabilidade", 75),
                Option("OTHER", "Outro", 50)
            ],

            "PAUSE" =>
            [
                Option("POWER_OUTAGE", "Falta de energia", 100),
                Option("EMERGENCY", "Emergência", 100),
                Option("INSTABILITY", "Instabilidade", 75),
                Option("MAINTENANCE", "Manutenção", 75),
                Option("HOLIDAY", "Feriado", 50),
                Option("OTHER", "Outro", 50)
            ],

            "CAMPAIGN" =>
            [
                Option("OTHER", "Campanha / divulgação", 25)
            ],

            _ => []
        };
    }

    public static AnnouncementReasonOption? Find(string? type, string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        return GetReasons(type).FirstOrDefault(option =>
            option.Value.Equals(reason, StringComparison.OrdinalIgnoreCase));
    }

    private static AnnouncementReasonOption Option(
        string value,
        string label,
        int priority)
    {
        var (priorityLabel, cssClass) = priority switch
        {
            >= 100 => ("Crítica", "priority-critical"),
            >= 75 => ("Alta", "priority-high"),
            >= 50 => ("Normal", "priority-normal"),
            _ => ("Baixa", "priority-low")
        };

        return new AnnouncementReasonOption(
            value,
            label,
            priority,
            priorityLabel,
            cssClass);
    }
}
