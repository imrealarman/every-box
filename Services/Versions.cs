using System.Text.RegularExpressions;

namespace EveryStore;

public static class Versions
{
    /// <summary>Reads the first dotted number in text such as "v1.2.3" or "EveryCopy 1.2", padded to four parts.</summary>
    public static bool TryParse(string? text, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        var match = Regex.Match(text, @"\d+(\.\d+){0,3}");
        if (!match.Success) return false;
        var parts = match.Value.Split('.').ToList();
        while (parts.Count < 4) parts.Add("0");
        return Version.TryParse(string.Join('.', parts), out version!);
    }

    /// <summary>True when <paramref name="candidate"/> is a higher version than <paramref name="current"/>. An unreadable current version counts as older.</summary>
    public static bool IsNewer(string? candidate, string? current)
    {
        if (!TryParse(candidate, out var next)) return false;
        if (!TryParse(current, out var now)) return true;
        return next > now;
    }

    /// <summary>"v1.2.0" and "1.2" both display as "1.2.0".</summary>
    public static string Display(string? text) =>
        TryParse(text, out var v) ? (v.Revision == 0 ? v.ToString(3) : v.ToString()) : (text ?? "");
}
