using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EveryStore;

public sealed partial class SettingsPage : UserControl
{
    private readonly MainWindow shell;
    private readonly AppSettings settings;

    public SettingsPage(MainWindow shell, AppSettings settings)
    {
        this.shell = shell;
        this.settings = settings;
        InitializeComponent();
        AboutText.Text = $"Every Store {StoreModel.StoreVersion} · by Arman Soltani";
        MarkTheme();
        TokenStatus.Text = string.IsNullOrEmpty(settings.Token) ? "No token saved." : "A token is saved.";
        AutoCheckSwitch.IsOn = settings.AutoCheck;
        AutoUpdateSwitch.IsOn = settings.AutoUpdate;
        MarkInterval();
    }

    private void MarkInterval()
    {
        IntervalRow.Opacity = settings.AutoCheck ? 1 : 0.4;
        AutoUpdateSwitch.IsEnabled = settings.AutoCheck;
        foreach (var b in new[] { Every1, Every6, Every24 })
        {
            b.IsEnabled = settings.AutoCheck;
            b.Style = (Style)Application.Current.Resources[(string)b.Tag == settings.CheckHours.ToString() ? "ChipButtonOn" : "ChipButton"];
        }
    }

    private void AutoCheck_Toggled(object sender, RoutedEventArgs e)
    {
        settings.AutoCheck = AutoCheckSwitch.IsOn;
        if (!settings.AutoCheck) { settings.AutoUpdate = false; AutoUpdateSwitch.IsOn = false; }
        Save();
    }

    private void AutoUpdate_Toggled(object sender, RoutedEventArgs e)
    {
        settings.AutoUpdate = AutoUpdateSwitch.IsOn;
        Save();
    }

    private void Interval_Click(object sender, RoutedEventArgs e)
    {
        settings.CheckHours = int.Parse((string)((Button)sender).Tag);
        Save();
    }

    private void Save()
    {
        settings.Save();
        MarkInterval();
        shell.ApplyAutoCheck();
    }

    private async void CheckNow_Click(object sender, RoutedEventArgs e)
    {
        CheckStatus.Text = "Checking…";
        await shell.AutoCheckAsync();
        int n = shell.Model.UpdateCount;
        CheckStatus.Text = n == 0 ? "Everything is up to date." : $"{n} {(n == 1 ? "update is" : "updates are")} available.";
    }

    private void MarkTheme()
    {
        foreach (var b in new[] { ThemeSystem, ThemeLight, ThemeDark })
            b.Style = (Style)Application.Current.Resources[(string)b.Tag == settings.Theme ? "ChipButtonOn" : "ChipButton"];
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        settings.Theme = (string)((Button)sender).Tag;
        settings.Save();
        shell.ApplyTheme();
        MarkTheme();
    }

    private async void SaveToken_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TokenBox.Password)) { TokenStatus.Text = "Paste a token first."; return; }
        settings.Token = TokenBox.Password;
        settings.Save();
        TokenBox.Password = "";
        TokenStatus.Text = "Token saved. Refreshing…";
        await shell.RefreshAsync(true);
        TokenStatus.Text = "Token saved.";
    }

    private void RemoveToken_Click(object sender, RoutedEventArgs e)
    {
        settings.Token = null;
        settings.Save();
        TokenStatus.Text = "Token removed.";
    }

    private async void ClearCache_Click(object sender, RoutedEventArgs e)
    {
        GitHubClient.ClearCache();
        StorageStatus.Text = "Saved data cleared. Reloading…";
        await shell.RefreshAsync(true);
        StorageStatus.Text = "Saved data cleared.";
    }

    private void OpenDownloads_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(Installer.DownloadsFolder) { UseShellExecute = true }); } catch { }
    }
}
