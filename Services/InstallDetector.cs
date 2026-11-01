using Microsoft.Win32;

namespace EveryStore;

public sealed record InstalledInfo(string Version, string? InstallDir, string? UninstallCommand, bool AllUsers);

/// <summary>
/// Finds out whether an app is installed. Every app installs with Inno Setup, which registers the app under
/// Uninstall\{AppId}_is1 for the current user (HKCU) or for all users (HKLM).
/// </summary>
public static class InstallDetector
{
    private const string UninstallRoot = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    public static InstalledInfo? Find(string? uninstallId)
    {
        if (string.IsNullOrEmpty(uninstallId)) return null;
        string sub = $@"{UninstallRoot}\{{{uninstallId}}}_is1";
        return Read(RegistryHive.CurrentUser, RegistryView.Default, sub, false)
            ?? Read(RegistryHive.LocalMachine, RegistryView.Registry64, sub, true)
            ?? Read(RegistryHive.LocalMachine, RegistryView.Registry32, sub, true);
    }

    private static InstalledInfo? Read(RegistryHive hive, RegistryView view, string sub, bool allUsers)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var key = root.OpenSubKey(sub);
            if (key is null) return null;
            string? dir = (key.GetValue("InstallLocation") as string)?.Trim().TrimEnd('\\');
            string? cmd = key.GetValue("QuietUninstallString") as string ?? key.GetValue("UninstallString") as string;
            return new InstalledInfo(key.GetValue("DisplayVersion") as string ?? "", string.IsNullOrEmpty(dir) ? null : dir, cmd, allUsers);
        }
        catch { return null; }
    }
}
