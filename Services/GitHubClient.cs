using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace EveryStore;

public sealed class GitHubException : Exception
{
    public HttpStatusCode? Status { get; }
    public GitHubException(string message, HttpStatusCode? status = null, Exception? inner = null) : base(message, inner) => Status = status;
}

/// <summary>A response plus when it was fetched. <see cref="Stale"/> means GitHub could not be reached and this is the saved copy.</summary>
public sealed record Cached<T>(T Value, DateTimeOffset FetchedAt, bool Stale);

/// <summary>
/// Reads releases, issues and repository details from the GitHub REST API. Responses are cached on disk so the
/// store opens instantly, stays inside the anonymous rate limit, and still shows data when offline.
/// </summary>
public sealed class GitHubClient
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly Func<string?> token;

    public GitHubClient(Func<string?> token)
    {
        this.token = token;
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("EveryStore", "1.0"));
        http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public static string CacheFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EveryStore", "cache");

    public static void ClearCache()
    {
        try { if (Directory.Exists(CacheFolder)) Directory.Delete(CacheFolder, true); } catch { }
    }

    public Task<Cached<RepoInfo>> GetRepoAsync(string repo, bool force, CancellationToken ct = default) =>
        GetJsonAsync<RepoInfo>($"repos/{repo}", Key(repo, "repo"), force, ct);

    public async Task<Cached<List<ReleaseInfo>>> GetReleasesAsync(string repo, bool force, CancellationToken ct = default)
    {
        var got = await GetJsonAsync<List<ReleaseInfo>>($"repos/{repo}/releases?per_page=30", Key(repo, "releases"), force, ct);
        return got with { Value = got.Value.Where(r => !r.Draft).OrderByDescending(r => r.PublishedAt).ToList() };
    }

    /// <summary>Issues and pull requests share a numbering; pull requests are filtered out here.</summary>
    public async Task<Cached<List<IssueInfo>>> GetIssuesAsync(string repo, bool force, CancellationToken ct = default)
    {
        var got = await GetJsonAsync<List<IssueInfo>>($"repos/{repo}/issues?state=all&per_page=100", Key(repo, "issues"), force, ct);
        return got with { Value = got.Value.Where(i => i.PullRequest is null).ToList() };
    }

    /// <summary>The repository README as markdown, or null when the repository has none.</summary>
    public async Task<string?> GetReadmeAsync(string repo, bool force, CancellationToken ct = default)
    {
        try
        {
            var (body, _, _) = await FetchAsync($"repos/{repo}/readme", Key(repo, "readme"), force, "application/vnd.github.raw+json", ct);
            return body;
        }
        catch (GitHubException ex) when (ex.Status == HttpStatusCode.NotFound) { return null; }
    }

    /// <summary>Posts a new issue as the token's user. Needs a token with issue write access to that repository.</summary>
    public async Task<IssueInfo> CreateIssueAsync(string repo, string title, string body, IEnumerable<string> labels, CancellationToken ct = default)
    {
        var tk = token();
        if (string.IsNullOrEmpty(tk)) throw new GitHubException("Add a GitHub token in Settings to send reports from the store.");
        using var req = new HttpRequestMessage(HttpMethod.Post, $"https://api.github.com/repos/{repo}/issues")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { title, body, labels }), Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tk);
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var resp = await SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw Describe(resp, text);
        return JsonSerializer.Deserialize<IssueInfo>(text, GitHubJson.Options)!;
    }

    private static string Key(string repo, string kind) => repo.Replace('/', '_') + "." + kind;

    private async Task<Cached<T>> GetJsonAsync<T>(string path, string key, bool force, CancellationToken ct) where T : new()
    {
        try
        {
            var (body, at, stale) = await FetchAsync(path, key, force, "application/vnd.github+json", ct);
            // An empty list is what a repo with no releases looks like; a missing body means nothing to parse.
            var value = string.IsNullOrWhiteSpace(body) ? new T() : JsonSerializer.Deserialize<T>(body, GitHubJson.Options) ?? new T();
            return new Cached<T>(value, at, stale);
        }
        catch (JsonException ex) { throw new GitHubException("GitHub sent a reply the store couldn't read.", null, ex); }
    }

    private async Task<(string Body, DateTimeOffset At, bool Stale)> FetchAsync(string path, string key, bool force, string accept, CancellationToken ct)
    {
        var file = Path.Combine(CacheFolder, key + ".json");
        var saved = ReadCache(file);
        if (saved is not null && !force && DateTimeOffset.UtcNow - saved.FetchedAt < DefaultTtl)
            return (saved.Body, saved.FetchedAt, false);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/" + path);
            req.Headers.Accept.ParseAdd(accept);
            var tk = token();
            if (!string.IsNullOrEmpty(tk)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tk);
            using var resp = await SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode) throw Describe(resp, body);
            var now = DateTimeOffset.UtcNow;
            WriteCache(file, new CacheEntry { FetchedAt = now, Body = body });
            return (body, now, false);
        }
        catch (Exception ex) when (saved is not null && ex is not OperationCanceledException || saved is not null && ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            return (saved!.Body, saved.FetchedAt, true); // offline or rate limited: show what we had
        }
        catch (HttpRequestException ex)
        {
            throw new GitHubException("Couldn't reach GitHub. Check your internet connection.", null, ex);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new GitHubException("GitHub took too long to answer.");
        }
    }

    private Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct) => http.SendAsync(req, ct);

    private static GitHubException Describe(HttpResponseMessage resp, string body)
    {
        bool limited = resp.StatusCode == HttpStatusCode.TooManyRequests ||
                       (resp.StatusCode == HttpStatusCode.Forbidden && resp.Headers.TryGetValues("X-RateLimit-Remaining", out var left) && left.FirstOrDefault() == "0");
        if (limited) return new GitHubException("GitHub's hourly limit for anonymous use is used up. Add a free token in Settings, or try again later.", resp.StatusCode);
        if (resp.StatusCode == HttpStatusCode.NotFound) return new GitHubException("Not found on GitHub.", resp.StatusCode);
        if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) return new GitHubException("GitHub rejected the token. Check it in Settings.", resp.StatusCode);
        string detail = "";
        try { detail = JsonDocument.Parse(body).RootElement.GetProperty("message").GetString() ?? ""; } catch { }
        return new GitHubException($"GitHub returned {(int)resp.StatusCode}. {detail}".Trim(), resp.StatusCode);
    }

    private sealed class CacheEntry
    {
        public DateTimeOffset FetchedAt { get; set; }
        public string Body { get; set; } = "";
    }

    private static CacheEntry? ReadCache(string file)
    {
        try { return File.Exists(file) ? JsonSerializer.Deserialize<CacheEntry>(File.ReadAllText(file)) : null; }
        catch { return null; }
    }

    private static void WriteCache(string file, CacheEntry entry)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(entry));
        }
        catch { /* the cache is an optimisation */ }
    }
}
