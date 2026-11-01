using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace EveryStore;

public enum ReportKind { Bug, Feedback }

/// <summary>What the user typed into the bug report or feedback form.</summary>
public sealed record Report(
    ReportKind Kind, string Title, string Details, string? Steps, int? Rating,
    bool IncludeDiagnostics, string AppName, string? InstalledVersion, string StoreVersion);

/// <summary>
/// Turns reports into GitHub issues. Bugs and feedback share the app's issue tracker, told apart by a title prefix
/// and a label. A feedback issue carries a "Rating: n/5" line, which is how the store computes an average rating.
/// </summary>
public static class Reports
{
    private const int MaxUrlBody = 5000;

    public static string IssueTitle(Report r) => $"[{(r.Kind == ReportKind.Bug ? "Bug" : "Feedback")}] {r.Title.Trim()}";

    public static string[] Labels(ReportKind kind) => new[] { kind == ReportKind.Bug ? "bug" : "feedback" };

    public static string Body(Report r)
    {
        var sb = new StringBuilder();
        if (r.Kind == ReportKind.Feedback && r.Rating is int stars) sb.Append("Rating: ").Append(stars).Append("/5\n\n");
        sb.Append(r.Kind == ReportKind.Bug ? "### What happened\n" : "### Feedback\n").Append(r.Details.Trim()).Append("\n\n");
        if (r.Kind == ReportKind.Bug && !string.IsNullOrWhiteSpace(r.Steps))
            sb.Append("### Steps to reproduce\n").Append(r.Steps.Trim()).Append("\n\n");
        if (r.IncludeDiagnostics)
            sb.Append("---\n<sub>").Append(Diagnostics(r.AppName, r.InstalledVersion, r.StoreVersion)).Append("</sub>\n");
        return sb.ToString().TrimEnd() + "\n";
    }

    public static string Diagnostics(string appName, string? installedVersion, string storeVersion) =>
        $"{appName} {(string.IsNullOrEmpty(installedVersion) ? "(not installed)" : installedVersion)} · Every Store {storeVersion} · {RuntimeInformation.OSDescription} · {RuntimeInformation.OSArchitecture}";

    /// <summary>A GitHub "new issue" page with the report filled in, for people who haven't added a token.</summary>
    public static string NewIssueUrl(string repo, Report r)
    {
        string body = Body(r);
        if (body.Length > MaxUrlBody) body = body[..MaxUrlBody] + "\n…(shortened)";
        return $"https://github.com/{repo}/issues/new?title={Uri.EscapeDataString(IssueTitle(r))}&body={Uri.EscapeDataString(body)}&labels={Uri.EscapeDataString(Labels(r.Kind)[0])}";
    }

    public static bool IsFeedback(IssueInfo i) =>
        i.Title.StartsWith("[Feedback]", StringComparison.OrdinalIgnoreCase) || i.Labels.Any(l => l.Name.Equals("feedback", StringComparison.OrdinalIgnoreCase));

    public static bool IsBug(IssueInfo i) =>
        i.Title.StartsWith("[Bug]", StringComparison.OrdinalIgnoreCase) || i.Labels.Any(l => l.Name.Equals("bug", StringComparison.OrdinalIgnoreCase));

    /// <summary>The 1-5 value of a "Rating: n/5" line, or null.</summary>
    public static int? ParseRating(string? body)
    {
        if (body is null) return null;
        var m = Regex.Match(body, @"Rating:\s*([1-5])\s*/\s*5", RegexOptions.IgnoreCase);
        return m.Success ? int.Parse(m.Groups[1].Value) : null;
    }

    /// <summary>Average and count over the issues that carry a rating.</summary>
    public static (double? Average, int Count) Rating(IEnumerable<IssueInfo> issues)
    {
        var ratings = issues.Where(IsFeedback).Select(i => ParseRating(i.Body)).OfType<int>().ToList();
        return ratings.Count == 0 ? (null, 0) : (ratings.Average(), ratings.Count);
    }
}
