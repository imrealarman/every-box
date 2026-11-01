using System.Collections.ObjectModel;
using System.Diagnostics;

namespace EveryStore;

/// <summary>The store's state and actions: the app list, loading from GitHub, install / update / uninstall / download.</summary>
public sealed class StoreModel
{
    public StoreModel(AppSettings settings)
    {
        Settings = settings;
        Github = new GitHubClient(() => Settings.Token);
        foreach (var app in CatalogApp.Load()) Apps.Add(new AppEntry(app));
    }

    public AppSettings Settings { get; }
    public GitHubClient Github { get; }
    public ObservableCollection<AppEntry> Apps { get; } = new();
    public DateTimeOffset? LastRefreshed { get; private set; }

    /// <summary>Raised after a refresh or install so lists and the update count can redraw. Text for the notice bar, or null.</summary>
    public event Action<string?>? Refreshed;

    /// <summary>Raised when an install, update or uninstall changes what is installed.</summary>
    public event Action? Changed;

    public int UpdateCount => Apps.Count(a => a.HasUpdate);

    public static string StoreVersion => typeof(StoreModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    public async Task RefreshAsync(bool force)
    {
        foreach (var app in Apps) app.Installed = InstallDetector.Find(app.App.UninstallId);
        var results = await Task.WhenAll(Apps.Select(a => LoadAppAsync(a, force)));
        LastRefreshed = DateTimeOffset.UtcNow;

        string? notice = results.Any(r => r == Load.Stale) ? "Couldn't reach GitHub, so this is the last data the store saved."
            : Apps.Select(a => a.LoadError).FirstOrDefault(e => e is not null);
        Refreshed?.Invoke(notice);
    }

    private enum Load { Fresh, Stale, Failed }

    private async Task<Load> LoadAppAsync(AppEntry e, bool force)
    {
        try
        {
            var repo = Github.GetRepoAsync(e.App.Repo, force);
            var releases = Github.GetReleasesAsync(e.App.Repo, force);
            await Task.WhenAll(repo, releases);
            e.Repo = repo.Result.Value;
            e.Releases = releases.Result.Value;
            e.LoadError = null;
            return repo.Result.Stale || releases.Result.Stale ? Load.Stale : Load.Fresh;
        }
        catch (GitHubException ex)
        {
            e.LoadError = ex.Message;
            return Load.Failed;
        }
        finally { e.Touch(); }
    }

    public async Task LoadIssuesAsync(AppEntry e, bool force)
    {
        try
        {
            var got = await Github.GetIssuesAsync(e.App.Repo, force);
            e.Issues = got.Value.OrderByDescending(i => i.CreatedAt).ToList();
            e.IssuesError = null;
        }
        catch (GitHubException ex) { e.IssuesError = ex.Message; }
        e.IssuesLoaded = true;
        e.Touch();
    }

    /// <summary>Downloads the latest release's installer and installs it. Also used to update.</summary>
    public async Task InstallAsync(AppEntry e, bool unattended = false)
    {
        if (e.Busy || e.LatestAsset is not { } asset) return;

        // Setup can't overwrite an app that is running (and can't close tray apps by itself), so close it first.
        var running = Installer.FindRunning(e.App.Exe, e.Installed?.InstallDir);
        if (running.Count > 0)
        {
            // An automatic update never closes something the user is using; it waits for the next check.
            bool ok = !unattended && ConfirmClose is not null && await ConfirmClose(e);
            if (!ok)
            {
                foreach (var p in running) p.Dispose();
                if (unattended) { e.ActionError = "Update is waiting for you to close the app."; e.Touch(); }
                return;
            }
        }
        bool relaunch = running.Count > 0;

        await RunAsync(e, async (progress, ct) =>
        {
            if (running.Count > 0)
            {
                progress.Report(new($"Closing {e.Name}…", null));
                await Installer.CloseAsync(running, ct);
            }
            await Installer.InstallAsync(asset, e.Installed?.AllUsers ?? false, progress, ct);
            e.Installed = InstallDetector.Find(e.App.UninstallId);
            if (e.Installed is null) throw new InstallException("The installer finished but Windows doesn't list the app. Try again.");
        });
        if (relaunch && e.ActionError is null) Launch(e);
    }

    /// <summary>Asks the user whether a running app may be closed so it can be installed or updated. Set by the window.</summary>
    public Func<AppEntry, Task<bool>>? ConfirmClose { get; set; }

    /// <summary>Installs every available update except for apps that are open right now. Returns how many were updated.</summary>
    public async Task<int> AutoUpdateAsync()
    {
        int updated = 0;
        foreach (var app in Apps.Where(a => a.HasUpdate && !a.Busy).ToList())
        {
            await InstallAsync(app, unattended: true);
            if (!app.HasUpdate && app.ActionError is null) updated++;
        }
        return updated;
    }

    /// <summary>Downloads the installer into the Downloads folder without installing it. Returns the saved file, or null if it failed.</summary>
    public async Task<string?> DownloadOnlyAsync(AppEntry e)
    {
        if (e.Busy || e.LatestAsset is not { } asset) return null;
        string? saved = null;
        await RunAsync(e, async (progress, ct) => saved = await Installer.DownloadAsync(asset, Installer.DownloadsFolder, progress, ct));
        return saved;
    }

    public async Task UninstallAsync(AppEntry e)
    {
        if (e.Busy || e.Installed?.UninstallCommand is not { } command) return;
        await RunAsync(e, async (progress, ct) =>
        {
            progress.Report(new("Uninstallingâ€¦", null));
            var running = Installer.FindRunning(e.App.Exe, e.Installed?.InstallDir);
            if (running.Count > 0) await Installer.CloseAsync(running, ct);
            await Installer.UninstallAsync(command, e.Installed?.AllUsers ?? false, ct);
            e.Installed = InstallDetector.Find(e.App.UninstallId);
        });
    }

    public async Task UpdateAllAsync()
    {
        foreach (var app in Apps.Where(a => a.HasUpdate).ToList()) await InstallAsync(app);
    }

    public void Launch(AppEntry e)
    {
        if (e.Installed?.InstallDir is not { } dir || e.App.Exe is not { } exe) return;
        string path = Path.Combine(dir, exe);
        try { if (File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = dir }); }
        catch (Exception ex) { e.ActionError = "Couldn't open the app: " + ex.Message; e.Touch(); }
    }

    private async Task RunAsync(AppEntry e, Func<IProgress<InstallProgress>, CancellationToken, Task> work)
    {
        e.Busy = true;
        e.ActionError = null;
        e.Phase = "Startingâ€¦";
        e.Percent = null;
        e.Cancel = new CancellationTokenSource();
        e.Touch();
        // Progress<T> posts back to the UI thread, which is where this method was called from.
        var progress = new Progress<InstallProgress>(p => { e.Phase = p.Phase; e.Percent = p.Percent; e.Touch(); });
        try { await work(progress, e.Cancel.Token); }
        catch (OperationCanceledException) { e.ActionError = "Cancelled."; }
        catch (InstallException ex) { e.ActionError = ex.Message; }
        catch (Exception ex) { e.ActionError = "Something went wrong: " + ex.Message; }
        finally
        {
            e.Busy = false;
            e.Cancel?.Dispose();
            e.Cancel = null;
            e.Touch();
            Changed?.Invoke();
        }
    }
}

