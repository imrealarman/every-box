using System.Text.Json;

namespace EveryStore.Tests;

public class VersionTests
{
    [Theory]
    [InlineData("v1.1.0", "1.0.0", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.0", "1.0.0", false)]   // padded, so equal rather than older
    [InlineData("v2", "1.9.9", true)]
    [InlineData("1.0.0", "1.2.0", false)]
    [InlineData("1.0.0", "", true)]       // unreadable installed version counts as older
    [InlineData("nightly", "1.0.0", false)]
    public void IsNewer(string candidate, string current, bool expected) =>
        Assert.Equal(expected, Versions.IsNewer(candidate, current));

    [Theory]
    [InlineData("v1.2.0", "1.2.0")]
    [InlineData("EveryCopy 1.2", "1.2.0")]
    [InlineData("1.2.3.4", "1.2.3.4")]
    public void Display(string input, string expected) => Assert.Equal(expected, Versions.Display(input));
}

public class InstallerTests
{
    private static ReleaseInfo Release(params string[] names) =>
        new() { Assets = names.Select(n => new AssetInfo { Name = n }).ToList() };

    [Fact]
    public void PickAsset_prefers_the_setup_exe() =>
        Assert.Equal("App-Setup-1.0.0.exe", Installer.PickAsset(Release("notes.txt", "App-Portable.exe", "App-Setup-1.0.0.exe"), "Setup")!.Name);

    [Fact]
    public void PickAsset_falls_back_to_a_single_exe() =>
        Assert.Equal("App.exe", Installer.PickAsset(Release("App.exe", "App.zip"), "Setup")!.Name);

    [Fact]
    public void PickAsset_returns_null_when_ambiguous_or_missing()
    {
        Assert.Null(Installer.PickAsset(Release("A.exe", "B.exe"), "Setup"));
        Assert.Null(Installer.PickAsset(Release("A.zip"), "Setup"));
    }

    [Fact]
    public void DigestMatches_accepts_missing_digest_and_rejects_mismatch()
    {
        Assert.True(Installer.DigestMatches(null, "ABC"));
        Assert.True(Installer.DigestMatches("sha256:abc123", "ABC123"));
        Assert.False(Installer.DigestMatches("sha256:abc123", "FFFFFF"));
        Assert.False(Installer.DigestMatches("md5:abc123", "ABC123")); // unknown algorithm is not trusted
    }

    [Theory]
    [InlineData(5, "still running")]   // the exact failure seen when Setup can't close a running app
    [InlineData(2, "cancelled")]
    [InlineData(8, "restart")]
    [InlineData(99, "code 99")]
    public void Explain_turns_exit_codes_into_sentences(int code, string fragment) =>
        Assert.Contains(fragment, Installer.Explain(code), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void FindRunning_ignores_blank_exe_and_apps_that_are_not_running()
    {
        Assert.Empty(Installer.FindRunning(null, null));
        Assert.Empty(Installer.FindRunning("NoSuchEveryStoreApp.exe", @"C:\nowhere"));
    }

    [Fact]
    public void SplitCommand_handles_quoted_paths()
    {
        Assert.Equal((@"C:\Users\a b\unins000.exe", "/SILENT"), Installer.SplitCommand("\"C:\\Users\\a b\\unins000.exe\" /SILENT"));
        Assert.Equal((@"C:\x\unins000.exe", ""), Installer.SplitCommand(@"C:\x\unins000.exe"));
    }
}

public class ReportTests
{
    private static Report Make(ReportKind kind, int? rating = null, bool diag = true) =>
        new(kind, " Crash on start ", "It closes.", "1. Open it", rating, diag, "EveryCopy", "1.0.0", "1.0.0");

    [Fact]
    public void Title_is_prefixed_and_trimmed()
    {
        Assert.Equal("[Bug] Crash on start", Reports.IssueTitle(Make(ReportKind.Bug)));
        Assert.Equal("[Feedback] Crash on start", Reports.IssueTitle(Make(ReportKind.Feedback, 4)));
    }

    [Fact]
    public void Feedback_body_round_trips_the_rating()
    {
        var body = Reports.Body(Make(ReportKind.Feedback, 4));
        Assert.Equal(4, Reports.ParseRating(body));
    }

    [Fact]
    public void Bug_body_has_steps_and_diagnostics_only_when_asked()
    {
        var with = Reports.Body(Make(ReportKind.Bug));
        Assert.Contains("Steps to reproduce", with);
        Assert.Contains("Every Store", with);
        Assert.DoesNotContain("Every Store", Reports.Body(Make(ReportKind.Bug, diag: false)));
        Assert.Null(Reports.ParseRating(with));
    }

    [Fact]
    public void NewIssueUrl_is_escaped_and_bounded()
    {
        var r = Make(ReportKind.Bug) with { Details = new string('x', 20000) };
        var url = Reports.NewIssueUrl("me/app", r);
        Assert.StartsWith("https://github.com/me/app/issues/new?title=%5BBug%5D", url);
        Assert.EndsWith("&labels=bug", url);
        Assert.True(url.Length < 8000);
    }

    [Fact]
    public void Rating_averages_only_feedback_issues_with_a_rating()
    {
        IssueInfo Issue(string title, string? body) => new() { Title = title, Body = body };
        var issues = new[]
        {
            Issue("[Feedback] great", "Rating: 5/5"),
            Issue("[Feedback] ok", "Rating: 3/5"),
            Issue("[Feedback] no stars", "just words"),
            Issue("[Bug] crash", "Rating: 1/5"), // a bug is never counted as a review
        };
        var (avg, count) = Reports.Rating(issues);
        Assert.Equal(2, count);
        Assert.Equal(4.0, avg);
        Assert.Equal((null, 0), Reports.Rating(Array.Empty<IssueInfo>()));
    }
}

public class GitHubJsonTests
{
    [Fact]
    public void Release_with_asset_digest_parses()
    {
        const string json = """
        [{"tag_name":"v1.0.0","name":"EveryCopy 1.0.0","draft":false,"prerelease":false,"published_at":"2026-11-01T07:37:35Z",
          "html_url":"https://github.com/x/y/releases/tag/v1.0.0",
          "assets":[{"name":"EveryCopy-Setup-1.0.0.exe","size":44843103,"download_count":7,"browser_download_url":"https://example/a.exe","digest":"sha256:e90a"}]}]
        """;
        var releases = JsonSerializer.Deserialize<List<ReleaseInfo>>(json, GitHubJson.Options)!;
        Assert.Equal("v1.0.0", releases[0].TagName);
        Assert.Equal(44843103, releases[0].Assets[0].Size);
        Assert.Equal("sha256:e90a", releases[0].Assets[0].Digest);
    }

    [Fact]
    public void Pull_requests_are_recognised_by_the_pull_request_field()
    {
        var issues = JsonSerializer.Deserialize<List<IssueInfo>>(
            """[{"number":1,"title":"a","pull_request":{"url":"u"}},{"number":2,"title":"b"}]""", GitHubJson.Options)!;
        Assert.NotNull(issues[0].PullRequest);
        Assert.Null(issues[1].PullRequest);
    }
}

public class TimeTextTests
{
    [Fact]
    public void Ago_uses_the_largest_sensible_unit()
    {
        var now = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal("just now", TimeText.Ago(now.AddSeconds(-10), now));
        Assert.Equal("1 minute ago", TimeText.Ago(now.AddSeconds(-70), now));
        Assert.Equal("3 hours ago", TimeText.Ago(now.AddHours(-3), now));
        Assert.Equal("2 days ago", TimeText.Ago(now.AddDays(-2), now));
    }

    [Fact]
    public void Size_formats_megabytes() => Assert.Equal("42.8 MB", TimeText.Size(44843103));
}
