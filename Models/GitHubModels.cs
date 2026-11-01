using System.Text.Json;
using System.Text.Json.Serialization;

namespace EveryStore;

// Just the fields of the GitHub REST responses the store uses. Property names map from snake_case.

public sealed class RepoInfo
{
    public string FullName { get; set; } = "";
    public string? Description { get; set; }
    public string HtmlUrl { get; set; } = "";
    public string? Homepage { get; set; }
    public int StargazersCount { get; set; }
    public DateTimeOffset? PushedAt { get; set; }
    public LicenseInfo? License { get; set; }
    public OwnerInfo Owner { get; set; } = new();
}

public sealed class LicenseInfo
{
    public string? Name { get; set; }
    public string? SpdxId { get; set; }
}

public sealed class OwnerInfo
{
    public string Login { get; set; } = "";
    public string HtmlUrl { get; set; } = "";
    public string AvatarUrl { get; set; } = "";
}

public sealed class ReleaseInfo
{
    public string TagName { get; set; } = "";
    public string? Name { get; set; }
    public string? Body { get; set; }
    public bool Draft { get; set; }
    public bool Prerelease { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public string HtmlUrl { get; set; } = "";
    public List<AssetInfo> Assets { get; set; } = new();
}

public sealed class AssetInfo
{
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public int DownloadCount { get; set; }
    public string BrowserDownloadUrl { get; set; } = "";
    /// <summary>"sha256:&lt;hex&gt;", filled in by GitHub for assets uploaded recently; null on old ones.</summary>
    public string? Digest { get; set; }
}

public sealed class IssueInfo
{
    public int Number { get; set; }
    public string Title { get; set; } = "";
    public string State { get; set; } = "open";
    public string HtmlUrl { get; set; } = "";
    public string? Body { get; set; }
    public int Comments { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<LabelInfo> Labels { get; set; } = new();
    /// <summary>Present when the "issue" is really a pull request; the issues endpoint returns both.</summary>
    public JsonElement? PullRequest { get; set; }
}

public sealed class LabelInfo
{
    public string Name { get; set; } = "";
    public string? Color { get; set; }
}

public static class GitHubJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };
}
