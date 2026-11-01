using System.Diagnostics;
using System.Security.Cryptography;

namespace EveryStore;

public readonly record struct InstallProgress(string Phase, double? Percent);

public sealed class InstallException : Exception
{
    public InstallException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Downloads a release's Setup file, checks it, and runs it silently (or uninstalls the app).</summary>
public static class Installer
{
    // No overall timeout: the installers are 40-70 MB and cancelling is the user's call.
    private static readonly HttpClient Download = CreateDownloadClient();

    private static HttpClient CreateDownloadClient()
    {
        var c = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("EveryStore/1.0");
        return c;
    }

    public static string TempFolder => Path.Combine(Path.GetTempPath(), "EveryStore");
    public static string DownloadsFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    /// <summary>The release's installer: the first .exe whose name contains <paramref name="match"/>, falling back to the only .exe.</summary>
    public static AssetInfo? PickAsset(ReleaseInfo release, string match)
    {
        var exes = release.Assets.Where(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)).ToList();
        return exes.FirstOrDefault(a => a.Name.Contains(match, StringComparison.OrdinalIgnoreCase))
            ?? (exes.Count == 1 ? exes[0] : null);
    }

    /// <summary>True when GitHub gave no digest, or when it matches the file's SHA-256 (hex, any case).</summary>
    public static bool DigestMatches(string? digest, string sha256Hex)
    {
        if (string.IsNullOrWhiteSpace(digest)) return true;
        var expected = digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : null;
        return expected is not null && string.Equals(expected, sha256Hex, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Splits a registry command such as <c>"C:\x y\unins000.exe" /SILENT</c> into program and arguments.</summary>
    public static (string Program, string Args) SplitCommand(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            if (end > 0) return (command[1..end], command[(end + 1)..].Trim());
        }
        int space = command.IndexOf(' ');
        return space < 0 ? (command, "") : (command[..space], command[(space + 1)..].Trim());
    }

    /// <summary>Downloads the asset to <paramref name="folder"/> and checks its size and SHA-256. Returns the file path.</summary>
    public static async Task<string> DownloadAsync(AssetInfo asset, string folder, IProgress<InstallProgress> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, asset.Name);
        string partial = target + ".part";
        try
        {
            progress.Report(new("Connecting to GitHub…", null));
            using var resp = await Download.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode) throw new InstallException($"The download failed (HTTP {(int)resp.StatusCode}).");
            long total = resp.Content.Headers.ContentLength ?? asset.Size;

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long done = 0;
            var lastReport = Stopwatch.StartNew();
            await using (var src = await resp.Content.ReadAsStreamAsync(ct))
            await using (var dst = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                int n;
                while ((n = await src.ReadAsync(buffer, ct)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                    hash.AppendData(buffer, 0, n);
                    done += n;
                    if (lastReport.ElapsedMilliseconds >= 100)
                    {
                        lastReport.Restart();
                        progress.Report(new($"Downloading {TimeText.Size(done)} of {TimeText.Size(total)}", total > 0 ? done * 100.0 / total : null));
                    }
                }
            }

            progress.Report(new("Verifying download…", null));
            if (asset.Size > 0 && done != asset.Size) throw new InstallException("The download was cut short. Try again.");
            if (!DigestMatches(asset.Digest, Convert.ToHexString(hash.GetHashAndReset())))
                throw new InstallException("The downloaded file doesn't match GitHub's checksum, so it was discarded.");

            File.Move(partial, target, true);
            return target;
        }
        catch (HttpRequestException ex) { throw new InstallException("Couldn't download the installer. Check your internet connection.", ex); }
        finally { try { File.Delete(partial); } catch { } }
    }

    /// <summary>Downloads the installer and runs it without any windows. An existing all-users install is updated in place (Windows asks for permission).</summary>
    /// <param name="allUsers">An all-users install lives in Program Files, so Setup must run elevated: Windows shows its permission (UAC) prompt.</param>
    public static async Task InstallAsync(AssetInfo asset, bool allUsers, IProgress<InstallProgress> progress, CancellationToken ct)
    {
        string file = await DownloadAsync(asset, TempFolder, progress, ct);
        try
        {
            progress.Report(new(allUsers ? "Waiting for permission…" : "Installing…", null));
            int code = await RunAsync(file, $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- {(allUsers ? "/ALLUSERS" : "/CURRENTUSER")}", allUsers, ct);
            if (code != 0) throw new InstallException(Explain(code));
        }
        finally { try { File.Delete(file); } catch { } }
    }

    public static async Task UninstallAsync(string uninstallCommand, bool elevate, CancellationToken ct)
    {
        var (program, _) = SplitCommand(uninstallCommand);
        if (!File.Exists(program)) throw new InstallException("The uninstaller is missing. Remove the app from Windows Settings > Apps instead.");
        int code = await RunAsync(program, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART", elevate, ct);
        if (code != 0) throw new InstallException($"The uninstaller stopped with code {code}.");
    }

    /// <summary>Inno Setup's exit codes in plain words.</summary>
    public static string Explain(int code) => code switch
    {
        1 => "The installer couldn't start.",
        2 or 5 => "The install was cancelled, or the app was still running and couldn't be closed. Close it and try again.",
        3 or 4 => "The installer hit an error while copying files.",
        7 or 8 => "Windows needs a restart before this can be installed.",
        _ => $"The installer stopped with code {code}.",
    };

    /// <summary>The app's processes still running, found by exe name and (when readable) install folder.</summary>
    public static List<Process> FindRunning(string? exe, string? installDir)
    {
        if (string.IsNullOrEmpty(exe)) return new();
        var found = new List<Process>();
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
        {
            string? path = null;
            try { path = p.MainModule?.FileName; } catch { /* elevated or exiting: can't tell, so go by name */ }
            if (path is null || installDir is null || path.StartsWith(installDir, StringComparison.OrdinalIgnoreCase)) found.Add(p);
            else p.Dispose();
        }
        return found;
    }

    /// <summary>Asks each process to close its window, waits a few seconds, then ends whatever is left (tray apps often ignore the request).</summary>
    public static async Task CloseAsync(List<Process> processes, CancellationToken ct)
    {
        foreach (var p in processes) { try { p.CloseMainWindow(); } catch { } }
        var deadline = DateTime.UtcNow.AddSeconds(4);
        while (DateTime.UtcNow < deadline && processes.Any(p => !p.HasExited)) await Task.Delay(150, ct);
        foreach (var p in processes)
        {
            try { if (!p.HasExited) { p.Kill(true); await p.WaitForExitAsync(ct); } }
            catch (InvalidOperationException) { /* already gone */ }
            catch (System.ComponentModel.Win32Exception ex) { throw new InstallException("Couldn't close the running app: " + ex.Message, ex); }
            finally { p.Dispose(); }
        }
    }

    private static async Task<int> RunAsync(string file, string args, bool elevate, CancellationToken ct)
    {
        try
        {
            // Elevating needs ShellExecute with the "runas" verb; a plain launch needs neither.
            var info = elevate
                ? new ProcessStartInfo(file, args) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden }
                : new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true };
            using var proc = Process.Start(info) ?? throw new InstallException("Windows wouldn't start the installer.");
            await proc.WaitForExitAsync(ct);
            return proc.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new InstallException("Administrator permission was declined, so nothing was installed.", ex);
        }
        catch (System.ComponentModel.Win32Exception ex) { throw new InstallException("Windows wouldn't start the installer: " + ex.Message, ex); }
    }
}
