namespace InsightHub.Api.Services.BotAnnouncements;

public static class AnnouncementPolicy
{
    public const int CriticalPriority = 100;
    public const int HighPriority = 75;
    public const int NormalPriority = 50;
    public const int LowPriority = 25;

    public static bool TryResolvePriority(
        string? type,
        string? reason,
        out int priority,
        out string? error)
    {
        priority = NormalPriority;
        error = null;

        var normalizedType = type?.Trim().ToUpperInvariant();
        var normalizedReason = string.IsNullOrWhiteSpace(reason)
            ? null
            : reason.Trim().ToUpperInvariant();

        priority = (normalizedType, normalizedReason) switch
        {
            ("INFO", null) => NormalPriority,
            ("INFO", "HOLIDAY") => NormalPriority,
            ("INFO", "MAINTENANCE") => NormalPriority,
            ("INFO", "OTHER") => LowPriority,

            ("WARNING", null) => HighPriority,
            ("WARNING", "INSTABILITY") => HighPriority,
            ("WARNING", "POWER_OUTAGE") => HighPriority,
            ("WARNING", "MAINTENANCE") => NormalPriority,
            ("WARNING", "EMERGENCY") => CriticalPriority,
            ("WARNING", "OTHER") => NormalPriority,

            ("MAINTENANCE", "MAINTENANCE") => HighPriority,
            ("MAINTENANCE", "INSTABILITY") => HighPriority,
            ("MAINTENANCE", "EMERGENCY") => CriticalPriority,
            ("MAINTENANCE", "OTHER") => NormalPriority,

            ("PAUSE", "POWER_OUTAGE") => CriticalPriority,
            ("PAUSE", "INSTABILITY") => HighPriority,
            ("PAUSE", "MAINTENANCE") => HighPriority,
            ("PAUSE", "HOLIDAY") => NormalPriority,
            ("PAUSE", "EMERGENCY") => CriticalPriority,
            ("PAUSE", "OTHER") => NormalPriority,

            ("CAMPAIGN", null) => LowPriority,
            ("CAMPAIGN", "OTHER") => LowPriority,

            _ => -1
        };

        if (priority >= 0)
        {
            return true;
        }

        error = "O motivo selecionado não é permitido para este tipo de comunicado.";
        priority = NormalPriority;
        return false;
    }

    public static string GetPriorityLevel(int priority) => priority switch
    {
        >= CriticalPriority => "CRITICAL",
        >= HighPriority => "HIGH",
        >= NormalPriority => "NORMAL",
        _ => "LOW"
    };
}
