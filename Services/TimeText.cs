namespace EveryStore;

public static class TimeText
{
    /// <summary>"just now", "5 minutes ago", "3 days ago", or the date once it is over a month old.</summary>
    public static string Ago(DateTimeOffset when, DateTimeOffset? now = null)
    {
        var span = (now ?? DateTimeOffset.UtcNow) - when;
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        if (span.TotalSeconds < 45) return "just now";
        if (span.TotalMinutes < 60) return Plural((int)Math.Max(1, Math.Round(span.TotalMinutes)), "minute");
        if (span.TotalHours < 24) return Plural((int)Math.Round(span.TotalHours), "hour");
        if (span.TotalDays < 30) return Plural((int)Math.Round(span.TotalDays), "day");
        return "on " + when.ToLocalTime().ToString("d MMM yyyy");
    }

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1048576.0:0.#} MB",
        _ => $"{bytes / 1073741824.0:0.##} GB",
    };

    private static string Plural(int n, string unit) => $"{n} {unit}{(n == 1 ? "" : "s")} ago";
}
