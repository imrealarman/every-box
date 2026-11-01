using System.Text.Json;

namespace EveryStore;

/// <summary>One app the store offers. The list lives in catalog.json next to the exe.</summary>
public sealed class CatalogApp
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>"owner/name" of the GitHub repository whose releases and issues the store reads.</summary>
    public string Repo { get; set; } = "";
    public string Tagline { get; set; } = "";
    public string Category { get; set; } = "";
    /// <summary>"installer" (a Setup .exe attached to the release) or "extension" (needs manual setup; the store only links to it).</summary>
    public string Kind { get; set; } = "installer";
    /// <summary>The Inno Setup AppId GUID without braces. Windows lists the installed app under this key, which is how the store tells it is installed.</summary>
    public string? UninstallId { get; set; }
    public string? Exe { get; set; }
    /// <summary>Picks the installer among a release's files: the first .exe whose name contains this text.</summary>
    public string AssetMatch { get; set; } = "Setup";
    /// <summary>Path of the icon inside the repository, loaded from raw.githubusercontent.com.</summary>
    public string? IconPath { get; set; }
    public string? Note { get; set; }

    public bool IsInstallable => Kind == "installer" && !string.IsNullOrEmpty(UninstallId);

    public string? IconUrl => string.IsNullOrEmpty(IconPath) ? null : $"https://raw.githubusercontent.com/{Repo}/HEAD/{IconPath}";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Reads catalog.json from the install folder, then lets %AppData%\EveryStore\catalog.json add apps or replace
    /// one with the same id, so a new app can be listed without rebuilding the store.
    /// </summary>
    public static List<CatalogApp> Load()
    {
        var apps = new List<CatalogApp>();
        Merge(apps, Path.Combine(AppContext.BaseDirectory, "catalog.json"));
        Merge(apps, Path.Combine(AppSettings.Folder, "catalog.json"));
        return apps;
    }

    private static void Merge(List<CatalogApp> apps, string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            var found = JsonSerializer.Deserialize<List<CatalogApp>>(File.ReadAllText(path), Json) ?? new();
            foreach (var app in found.Where(a => a.Id.Length > 0 && a.Repo.Contains('/')))
            {
                int at = apps.FindIndex(a => a.Id == app.Id);
                if (at >= 0) apps[at] = app; else apps.Add(app);
            }
        }
        catch { /* a broken catalog file just contributes nothing */ }
    }
}
