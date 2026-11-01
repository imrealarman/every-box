using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;

namespace EveryStore;

public sealed partial class MainWindow : Window
{
    private readonly AppSettings settings = AppSettings.Load();
    private readonly StoreModel model;
    private readonly Stack<UserControl> history = new();
    private string section = "discover";
    private string? notice;

    public MainWindow()
    {
        InitializeComponent();
        model = new StoreModel(settings);

        Title = "Every Store";
        AppWindow.Resize(new SizeInt32(1180, 780));
        string icon = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(icon)) AppWindow.SetIcon(icon);
        BrandIcon.Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.png")));

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleDrag);
        AppWindow.TitleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);

        ApplyTheme();
        Root.ActualThemeChanged += (_, _) => Palette.Apply(Root.ActualTheme == ElementTheme.Light);
        Root.Loaded += async (_, _) =>
        {
            Palette.Apply(Root.ActualTheme == ElementTheme.Light);
            ShowSection("discover");
            // On launch: use saved data at once, then (if enabled) look for new versions and install them.
            await RefreshAsync(false);
            if (settings.AutoCheck) await AutoCheckAsync();
        };

        model.Refreshed += n => { notice = n; ShowNotice(); UpdateRail(); };
        model.Changed += UpdateRail;
        model.ConfirmClose = ConfirmCloseAsync;

        checkTimer.Tick += async (_, _) => await AutoCheckAsync();
        ApplyAutoCheck();
    }

    private readonly Microsoft.UI.Xaml.DispatcherTimer checkTimer = new();

    /// <summary>(Re)starts the periodic check from the settings. Also called when the settings change.</summary>
    public void ApplyAutoCheck()
    {
        checkTimer.Stop();
        if (!settings.AutoCheck) { UpdateBar.IsOpen = false; return; }
        checkTimer.Interval = TimeSpan.FromHours(Math.Clamp(settings.CheckHours, 1, 168));
        checkTimer.Start();
    }

    /// <summary>Looks for new versions, installs them if auto-update is on, and otherwise points at the Library.</summary>
    public async Task AutoCheckAsync()
    {
        await model.RefreshAsync(true);
        if (settings.AutoUpdate && model.UpdateCount > 0)
        {
            int done = await model.AutoUpdateAsync();
            if (done > 0) await model.RefreshAsync(false);
        }
        ShowUpdateBar();
    }

    private void ShowUpdateBar()
    {
        int n = model.UpdateCount;
        UpdateBar.IsOpen = settings.AutoCheck && n > 0;
        UpdateBar.Title = n == 1 ? "1 update is available" : $"{n} updates are available";
        UpdateBar.Message = settings.AutoUpdate ? "Apps that are open will update once you close them." : "Open the Library to install them.";
    }

    private void UpdateBar_Open(object sender, RoutedEventArgs e) => ShowSection("library");

    private async Task<bool> ConfirmCloseAsync(AppEntry entry)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            RequestedTheme = Root.ActualTheme,
            Title = $"{entry.Name} is running",
            Content = $"{entry.Name} has to be closed before it can be installed or updated. It will be reopened afterwards. Unsaved work in it, such as a transfer in progress, will be lost.",
            PrimaryButtonText = "Close it and continue",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public StoreModel Model => model;

    public void ApplyTheme()
    {
        Root.RequestedTheme = settings.Theme switch
        {
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        Palette.Apply(Root.ActualTheme == ElementTheme.Light);
    }

    public Task RefreshAsync(bool force) => model.RefreshAsync(force);

    // ---- navigation ----

    private void NavDiscover_Click(object sender, RoutedEventArgs e) => ShowSection("discover");
    private void NavLibrary_Click(object sender, RoutedEventArgs e) => ShowSection("library");
    private void NavSettings_Click(object sender, RoutedEventArgs e) => ShowSection("settings");

    private void ShowSection(string name)
    {
        section = name;
        history.Clear();
        Host.Content = name switch
        {
            "library" => new CatalogPage(this, libraryOnly: true),
            "settings" => new SettingsPage(this, settings),
            _ => new CatalogPage(this, libraryOnly: false),
        };
        UpdateRail();
    }

    public void OpenApp(AppEntry entry)
    {
        if (Host.Content is UserControl current) history.Push(current);
        Host.Content = new AppPage(this, entry);
    }

    public void GoBack()
    {
        if (history.Count > 0) Host.Content = history.Pop();
        else ShowSection(section);
    }

    private void UpdateRail()
    {
        int updates = model.UpdateCount;
        UpdateBadge.Visibility = updates > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateBadgeText.Text = updates.ToString();
        RefreshedText.Text = model.LastRefreshed is { } at ? $"Updated {TimeText.Ago(at)}" : "";

        void Mark(Button b, bool on) => b.Background = on ? (Brush)Application.Current.Resources["AccentTintBrush"] : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        Mark(NavDiscover, section == "discover");
        Mark(NavLibrary, section == "library");
        Mark(NavSettings, section == "settings");
    }

    private void ShowNotice()
    {
        Notice.Message = notice ?? "";
        Notice.IsOpen = notice is not null;
    }
}
