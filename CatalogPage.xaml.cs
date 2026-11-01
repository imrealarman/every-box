using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EveryStore;

/// <summary>Discover (every app) or Library (installed apps, with an Update all banner).</summary>
public sealed partial class CatalogPage : UserControl
{
    private readonly MainWindow shell;
    private readonly bool libraryOnly;

    public CatalogPage(MainWindow shell, bool libraryOnly)
    {
        this.shell = shell;
        this.libraryOnly = libraryOnly;
        InitializeComponent();
        TitleText.Text = libraryOnly ? "Library" : "Discover";
        Loaded += (_, _) => { shell.Model.Refreshed += OnRefreshed; shell.Model.Changed += Rebuild; Rebuild(); };
        Unloaded += (_, _) => { shell.Model.Refreshed -= OnRefreshed; shell.Model.Changed -= Rebuild; };
    }

    private void OnRefreshed(string? _) => Rebuild();

    private void Rebuild()
    {
        string query = SearchBox.Text.Trim();
        var shown = shell.Model.Apps
            .Where(a => !libraryOnly || a.IsInstalled)
            .Where(a => query.Length == 0 || a.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        a.Tagline.Contains(query, StringComparison.OrdinalIgnoreCase) || a.Category.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
        Cards.ItemsSource = shown;

        int updates = shell.Model.UpdateCount;
        SubtitleText.Text = libraryOnly
            ? $"{shell.Model.Apps.Count(a => a.IsInstalled)} installed"
            : $"{shell.Model.Apps.Count} apps · all open source, installed straight from their GitHub releases";
        UpdatesBar.Visibility = libraryOnly && updates > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdatesText.Text = updates == 1 ? "1 update is available" : $"{updates} updates are available";

        EmptyText.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = libraryOnly && query.Length == 0 ? "Nothing installed yet. Find an app in Discover." : "No apps match your search.";
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => Rebuild();

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await shell.RefreshAsync(true);

    private async void UpdateAll_Click(object sender, RoutedEventArgs e) => await shell.Model.UpdateAllAsync();

    private void Cards_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AppEntry entry) shell.OpenApp(entry);
    }

    /// <summary>The card's button: install, update, open, or (for apps that can't install here) the repository.</summary>
    private async void Action_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AppEntry entry) return;
        await Actions.RunPrimaryAsync(shell.Model, entry);
    }
}

/// <summary>What the main button does for each state; shared by the cards and the detail page.</summary>
public static class Actions
{
    public static async Task RunPrimaryAsync(StoreModel model, AppEntry entry)
    {
        switch (entry.State)
        {
            case AppState.NotInstalled:
            case AppState.UpdateAvailable:
                await model.InstallAsync(entry);
                break;
            case AppState.Installed:
                model.Launch(entry);
                break;
            default:
                await Windows.System.Launcher.LaunchUriAsync(new Uri(entry.RepoUrl));
                break;
        }
    }
}
