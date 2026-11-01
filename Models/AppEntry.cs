using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace EveryStore;

public enum AppState { Unavailable, NotInstalled, UpdateAvailable, Installed }

/// <summary>Everything the UI shows about one app: catalog entry, GitHub data, what's installed, and any install in progress.</summary>
public sealed class AppEntry : INotifyPropertyChanged
{
    public AppEntry(CatalogApp app) => App = app;

    public CatalogApp App { get; }

    // GitHub data
    public RepoInfo? Repo { get; set; }
    public List<ReleaseInfo> Releases { get; set; } = new();
    public List<IssueInfo> Issues { get; set; } = new();
    public bool IssuesLoaded { get; set; }
    public string? LoadError { get; set; }
    public string? IssuesError { get; set; }

    // Local state
    public InstalledInfo? Installed { get; set; }

    // Install in progress
    public bool Busy { get; set; }
    public string Phase { get; set; } = "";
    public double? Percent { get; set; }
    public string? ActionError { get; set; }
    public CancellationTokenSource? Cancel { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Tells every binding to re-read. Cheap, and the data changes in whole steps anyway.</summary>
    public void Touch() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    // ---- identity ----
    public string Name => App.Name;
    public string Tagline => App.Tagline.Length > 0 ? App.Tagline : Repo?.Description ?? "";
    public string Category => App.Category;
    public string Developer => Repo?.Owner.Login is { Length: > 0 } login ? login : App.Repo.Split('/')[0];
    public string DeveloperUrl => Repo?.Owner.HtmlUrl is { Length: > 0 } u ? u : $"https://github.com/{App.Repo.Split('/')[0]}";
    public string RepoUrl => Repo?.HtmlUrl is { Length: > 0 } u ? u : $"https://github.com/{App.Repo}";

    private ImageSource? icon;
    public ImageSource? IconSource
    {
        get
        {
            if (icon is null && App.IconUrl is { } url)
                icon = new BitmapImage(new Uri(url)) { DecodePixelWidth = 128 };
            return icon;
        }
    }

    // ---- releases ----
    public ReleaseInfo? Latest => Releases.FirstOrDefault(r => !r.Prerelease && Installer.PickAsset(r, App.AssetMatch) is not null);
    public AssetInfo? LatestAsset => Latest is null ? null : Installer.PickAsset(Latest, App.AssetMatch);
    public string LatestVersion => Latest is null ? "" : Versions.Display(Latest.TagName);
    public string InstalledVersion => Installed is null ? "" : Versions.Display(Installed.Version);
    public string ReleasedText => Latest?.PublishedAt is { } d ? $"Released {TimeText.Ago(d)}" : "No release yet";
    public string SizeText => LatestAsset is { } a ? TimeText.Size(a.Size) : "—";
    public int TotalDownloads => Releases.SelectMany(r => r.Assets).Sum(a => a.DownloadCount);

    public AppState State =>
        !App.IsInstallable || LatestAsset is null && Installed is null ? AppState.Unavailable
        : Installed is null ? AppState.NotInstalled
        : LatestAsset is not null && Versions.IsNewer(Latest!.TagName, Installed.Version) ? AppState.UpdateAvailable
        : AppState.Installed;

    public bool IsInstalled => Installed is not null;
    public bool HasUpdate => State == AppState.UpdateAvailable;

    // ---- rating and issues ----
    public (double? Average, int Count) RatingInfo => Reports.Rating(Issues);
    public string RatingText => RatingInfo is { Average: { } avg, Count: var n } ? $"★ {avg:0.0} ({n})" : "No ratings yet";
    public int OpenBugs => Issues.Count(i => i.State == "open" && Reports.IsBug(i));

    // ---- button and status text ----
    public string ActionLabel => State switch
    {
        AppState.NotInstalled => "Install",
        AppState.UpdateAvailable => "Update",
        AppState.Installed => "Open",
        _ => App.IsInstallable ? "Unavailable" : "View on GitHub",
    };

    public bool CanAct => !Busy && (State != AppState.Unavailable || !App.IsInstallable);

    public string StatusLine
    {
        get
        {
            if (Busy) return Percent is { } p ? $"{Phase}  {p:0}%" : Phase;
            if (ActionError is not null) return ActionError;
            return State switch
            {
                AppState.Installed => $"Installed · v{InstalledVersion}",
                AppState.UpdateAvailable => $"Installed v{InstalledVersion} · v{LatestVersion} available",
                AppState.NotInstalled => $"v{LatestVersion} · {ReleasedText}",
                _ when !App.IsInstallable => App.Category,
                _ => LoadError ?? "No release yet",
            };
        }
    }

    public Visibility ProgressVisibility => Busy ? Visibility.Visible : Visibility.Collapsed;
    public Visibility UpdateBadgeVisibility => HasUpdate ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ErrorVisibility => ActionError is null || Busy ? Visibility.Collapsed : Visibility.Visible;
    public bool ProgressIndeterminate => Percent is null;
    public double ProgressValue => Percent ?? 0;
}
