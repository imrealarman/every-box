using System.ComponentModel;
using System.Diagnostics;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace EveryStore;

/// <summary>One app: install / update / open, what's new, release history, and bug reports and feedback.</summary>
public sealed partial class AppPage : UserControl
{
    private readonly MainWindow shell;
    private readonly AppEntry entry;
    private readonly List<Button> tabs = new();
    private readonly Dictionary<string, UIElement> panels = new();
    private string tab = "overview";
    private string issueFilter = "all";
    private bool readmeRequested;
    private string? savedFile;

    public AppPage(MainWindow shell, AppEntry entry)
    {
        this.shell = shell;
        this.entry = entry;
        InitializeComponent();

        panels["overview"] = OverviewPanel;
        panels["new"] = WhatsNewPanel;
        panels["versions"] = VersionsPanel;
        panels["feedback"] = FeedbackPanel;
        foreach (var (key, label) in new[] { ("overview", "Overview"), ("new", "What's new"), ("versions", "Versions"), ("feedback", "Bugs & feedback") })
        {
            var b = new Button { Content = label, Tag = key };
            b.Click += (_, _) => SelectTab(key);
            tabs.Add(b);
            TabBar.Children.Add(b);
        }

        Loaded += (_, _) => { entry.PropertyChanged += OnEntryChanged; Render(); SelectTab(tab); };
        Unloaded += (_, _) => entry.PropertyChanged -= OnEntryChanged;
    }

    private void OnEntryChanged(object? sender, PropertyChangedEventArgs e) => Render();

    // ---- header, buttons, stats ----

    private void Render()
    {
        Icon.Source = entry.IconSource;
        NameText.Text = entry.Name;
        DeveloperLink.Content = "by " + entry.Developer;
        DeveloperLink.NavigateUri = new Uri(entry.DeveloperUrl);
        CategoryText.Text = "· " + entry.Category;
        TaglineText.Text = entry.Tagline;

        PrimaryBtn.Content = entry.State switch
        {
            AppState.NotInstalled => "Install",
            AppState.UpdateAvailable => $"Update to v{entry.LatestVersion}",
            _ => entry.ActionLabel,
        };
        PrimaryBtn.IsEnabled = entry.CanAct;
        DownloadBtn.Visibility = !entry.Busy && entry.LatestAsset is not null ? Visibility.Visible : Visibility.Collapsed;
        CancelBtn.Visibility = entry.Busy ? Visibility.Visible : Visibility.Collapsed;
        UninstallBtn.Visibility = !entry.Busy && entry.Installed?.UninstallCommand is not null ? Visibility.Visible : Visibility.Collapsed;

        Progress.Visibility = entry.ProgressVisibility;
        Progress.IsIndeterminate = entry.ProgressIndeterminate;
        Progress.Value = entry.ProgressValue;
        StatusText.Text = !entry.Busy && entry.ActionError is null && savedFile is not null ? $"Saved to {savedFile}"
            : !entry.App.IsInstallable && entry.App.Note is { } note ? note
            : entry.StatusLine;

        RenderStats();
    }

    private void RenderStats()
    {
        Stats.Children.Clear();
        Stats.ColumnDefinitions.Clear();
        var items = new List<(string Label, string Value)>
        {
            ("Latest version", entry.LatestVersion.Length > 0 ? "v" + entry.LatestVersion : "—"),
            ("Last released", entry.Latest?.PublishedAt is { } d ? TimeText.Ago(d).Replace(" ago", " ago") : "—"),
            ("Download size", entry.SizeText),
            ("Downloads", entry.Releases.Count == 0 ? "—" : entry.TotalDownloads.ToString("N0")),
            ("Rating", entry.IssuesLoaded ? entry.RatingText : "—"),
        };
        for (int i = 0; i < items.Count; i++)
        {
            Stats.ColumnDefinitions.Add(new ColumnDefinition());
            var tile = new Border
            {
                Padding = new Thickness(14, 10, 14, 10),
                CornerRadius = new CornerRadius(8),
                Background = (Brush)Application.Current.Resources["PanelBrush"],
                BorderBrush = (Brush)Application.Current.Resources["BorderBrush1"],
                BorderThickness = new Thickness(1),
                Child = new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock { Text = items[i].Label, Style = (Style)Application.Current.Resources["PanelHeader"] },
                        new TextBlock { Text = items[i].Value, FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap },
                    },
                },
            };
            Grid.SetColumn(tile, i);
            Stats.Children.Add(tile);
        }
    }

    private async void Primary_Click(object sender, RoutedEventArgs e)
    {
        savedFile = null;
        await Actions.RunPrimaryAsync(shell.Model, entry);
    }

    private async void DownloadOnly_Click(object sender, RoutedEventArgs e)
    {
        savedFile = await shell.Model.DownloadOnlyAsync(entry);
        if (savedFile is not null)
        {
            try { Process.Start("explorer.exe", $"/select,\"{savedFile}\""); } catch { }
        }
        Render();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => entry.Cancel?.Cancel();

    private async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Uninstall {entry.Name}?",
            Content = "The app is removed from this PC. Its settings are kept.",
            PrimaryButtonText = "Uninstall",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            RequestedTheme = ActualTheme,
        };
        if (await confirm.ShowAsync() == ContentDialogResult.Primary) await shell.Model.UninstallAsync(entry);
    }

    private void Back_Click(object sender, RoutedEventArgs e) => shell.GoBack();

    // ---- tabs ----

    private void SelectTab(string key)
    {
        tab = key;
        foreach (var b in tabs)
            b.Style = (Style)Application.Current.Resources[(string)b.Tag == key ? "ChipButtonOn" : "ChipButton"];
        foreach (var (k, panel) in panels) panel.Visibility = k == key ? Visibility.Visible : Visibility.Collapsed;

        switch (key)
        {
            case "overview": BuildOverview(); break;
            case "new": BuildWhatsNew(); break;
            case "versions": BuildVersions(); break;
            case "feedback": _ = ShowFeedbackAsync(false); break;
        }
    }

    private void BuildOverview()
    {
        OverviewPanel.Children.Clear();
        OverviewPanel.Children.Add(Section("Details"));
        OverviewPanel.Children.Add(Facts(new (string, string)[]
        {
            ("Developer", entry.Developer),
            ("License", entry.Repo?.License?.Name is { Length: > 0 } l && l != "Other" ? l : "Not specified"),
            ("Requires", "Windows 10 (1809) or later, 64-bit"),
            ("Source", entry.RepoUrl),
            ("Installed", entry.Installed is { } i ? $"v{entry.InstalledVersion}" + (i.InstallDir is null ? "" : $" · {i.InstallDir}") : "No"),
            ("Last updated", entry.Repo?.PushedAt is { } p ? TimeText.Ago(p) : "—"),
        }));

        OverviewPanel.Children.Add(Section("About"));
        var about = new StackPanel { Spacing = 6 };
        OverviewPanel.Children.Add(about);
        if (readmeRequested) { _ = LoadReadmeAsync(about, false); return; }
        readmeRequested = true;
        _ = LoadReadmeAsync(about, true);
    }

    private async Task LoadReadmeAsync(StackPanel target, bool first)
    {
        target.Children.Add(new TextBlock { Text = "Loading…", Style = (Style)Application.Current.Resources["MutedText"] });
        try
        {
            var md = await shell.Model.Github.GetReadmeAsync(entry.App.Repo, false);
            target.Children.Clear();
            target.Children.Add(MarkdownView.Build(md is null ? entry.Tagline : UserFacing(md)));
        }
        catch (GitHubException ex)
        {
            target.Children.Clear();
            target.Children.Add(new TextBlock { Text = entry.Tagline + "\n\n" + ex.Message, TextWrapping = TextWrapping.Wrap });
        }
    }

    private static readonly string[] TechnicalHeadings = ["build", "requirements", "tests", "testing", "project layout", "stack", "tech", "architecture", "development", "contributing"];

    /// <summary>Drops the README sections that are for people building the app (build steps, stack, layout), which don't belong in a store page.</summary>
    private static string UserFacing(string markdown)
    {
        var kept = new List<string>();
        var skipping = false;
        var fence = false;
        foreach (var line in markdown.Split('\n'))
        {
            if (line.TrimStart().StartsWith("```")) fence = !fence;
            if (!fence && line.StartsWith("## "))
            {
                var heading = line[3..].Trim().ToLowerInvariant();
                skipping = TechnicalHeadings.Any(heading.StartsWith);
            }
            if (!skipping) kept.Add(line);
        }
        return string.Join('\n', kept);
    }

    private void BuildWhatsNew()
    {
        WhatsNewPanel.Children.Clear();
        var rel = entry.Latest ?? entry.Releases.FirstOrDefault();
        if (rel is null)
        {
            WhatsNewPanel.Children.Add(new TextBlock { Text = "This app hasn't published a release yet.", Style = (Style)Application.Current.Resources["MutedText"] });
            return;
        }
        WhatsNewPanel.Children.Add(Section($"{rel.Name ?? rel.TagName}"));
        WhatsNewPanel.Children.Add(new TextBlock
        {
            Text = $"v{Versions.Display(rel.TagName)} · {(rel.PublishedAt is { } d ? "released " + TimeText.Ago(d) : "")}",
            Style = (Style)Application.Current.Resources["MutedText"],
        });
        WhatsNewPanel.Children.Add(MarkdownView.Build(rel.Body));
        WhatsNewPanel.Children.Add(new HyperlinkButton { Content = "View on GitHub", NavigateUri = new Uri(rel.HtmlUrl), Padding = new Thickness(0) });
    }

    private void BuildVersions()
    {
        VersionsPanel.Children.Clear();
        if (entry.Releases.Count == 0)
        {
            VersionsPanel.Children.Add(new TextBlock { Text = "No releases yet.", Style = (Style)Application.Current.Resources["MutedText"] });
            return;
        }
        foreach (var rel in entry.Releases)
        {
            var asset = Installer.PickAsset(rel, entry.App.AssetMatch);
            string meta = string.Join(" · ", new[]
            {
                rel.PublishedAt is { } d ? TimeText.Ago(d) : null,
                asset is null ? null : TimeText.Size(asset.Size),
                asset is null ? null : $"{asset.DownloadCount:N0} downloads",
                rel.Prerelease ? "pre-release" : null,
                entry.Installed is not null && Versions.TryParse(rel.TagName, out var rv) && Versions.TryParse(entry.Installed.Version, out var iv) && rv == iv ? "installed" : null,
            }.Where(s => s is not null));

            VersionsPanel.Children.Add(new Expander
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Header = new StackPanel
                {
                    Padding = new Thickness(0, 6, 0, 6),
                    Children =
                    {
                        new TextBlock { Text = rel.Name ?? rel.TagName, FontSize = 14, FontWeight = FontWeights.SemiBold },
                        new TextBlock { Text = meta, Style = (Style)Application.Current.Resources["MutedText"] },
                    },
                },
                Content = MarkdownView.Build(rel.Body),
            });
        }
    }

    // ---- bugs and feedback ----

    private async Task ShowFeedbackAsync(bool force)
    {
        if (!entry.IssuesLoaded || force)
        {
            RatingText.Text = "Loading…";
            await shell.Model.LoadIssuesAsync(entry, force);
        }
        BuildIssueList();
    }

    private void BuildIssueList()
    {
        RatingText.Text = entry.IssuesError ?? (entry.RatingInfo.Count > 0
            ? $"{entry.RatingText} from {entry.RatingInfo.Count} {(entry.RatingInfo.Count == 1 ? "review" : "reviews")} · {entry.OpenBugs} open {(entry.OpenBugs == 1 ? "bug" : "bugs")}"
            : $"No ratings yet · {entry.OpenBugs} open {(entry.OpenBugs == 1 ? "bug" : "bugs")}");

        IssueFilters.Children.Clear();
        foreach (var (key, label) in new[] { ("all", "All"), ("bugs", "Bugs"), ("feedback", "Feedback") })
        {
            var b = new Button { Content = label, Style = (Style)Application.Current.Resources[issueFilter == key ? "ChipButtonOn" : "ChipButton"] };
            b.Click += (_, _) => { issueFilter = key; BuildIssueList(); };
            IssueFilters.Children.Add(b);
        }

        IssueList.Children.Clear();
        var issues = entry.Issues.Where(i => issueFilter switch { "bugs" => Reports.IsBug(i), "feedback" => Reports.IsFeedback(i), _ => true }).ToList();
        if (issues.Count == 0)
        {
            IssueList.Children.Add(new TextBlock
            {
                Text = entry.IssuesError is null ? "Nothing reported yet. Be the first." : "",
                Style = (Style)Application.Current.Resources["MutedText"],
            });
            return;
        }
        foreach (var issue in issues) IssueList.Children.Add(IssueRow(issue));
    }

    private UIElement IssueRow(IssueInfo issue)
    {
        bool open = issue.State == "open";
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        line.Children.Add(Pill(open ? "Open" : "Closed", open));
        line.Children.Add(new TextBlock { Text = $"#{issue.Number} · {TimeText.Ago(issue.CreatedAt)}", Style = (Style)Application.Current.Resources["MutedText"], VerticalAlignment = VerticalAlignment.Center });
        if (Reports.ParseRating(issue.Body) is int stars)
            line.Children.Add(new TextBlock { Text = new string('★', stars) + new string('☆', 5 - stars), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        if (issue.Comments > 0)
            line.Children.Add(new TextBlock { Text = $"{issue.Comments} {(issue.Comments == 1 ? "comment" : "comments")}", Style = (Style)Application.Current.Resources["MutedText"], VerticalAlignment = VerticalAlignment.Center });

        var button = new Button
        {
            Style = (Style)Application.Current.Resources["TileButton"],
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(14, 10, 14, 10),
            Content = new StackPanel
            {
                Spacing = 4,
                Children = { new TextBlock { Text = issue.Title, FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap }, line },
            },
        };
        button.Click += async (_, _) => await Launcher.LaunchUriAsync(new Uri(issue.HtmlUrl));
        return button;
    }

    private static Border Pill(string text, bool on) => new()
    {
        CornerRadius = new CornerRadius(9),
        Padding = new Thickness(8, 1, 8, 1),
        Background = on ? (Brush)Application.Current.Resources["AccentTintBrush"] : (Brush)Application.Current.Resources["TileBrush"],
        Child = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold },
    };

    private async void ReportBug_Click(object sender, RoutedEventArgs e) => await ReportAsync(ReportKind.Bug);
    private async void SendFeedback_Click(object sender, RoutedEventArgs e) => await ReportAsync(ReportKind.Feedback);

    private async Task ReportAsync(ReportKind kind)
    {
        if (await ReportDialog.ShowAsync(XamlRoot, ActualTheme, shell.Model, entry, kind))
            await ShowFeedbackAsync(true);
    }

    // ---- small builders ----

    private static TextBlock Section(string text) =>
        new() { Text = text, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0) };

    private static Grid Facts(IEnumerable<(string Label, string Value)> facts)
    {
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 6 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        int row = 0;
        foreach (var (label, value) in facts)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = new TextBlock { Text = label, Style = (Style)Application.Current.Resources["MutedText"], FontSize = 12 };
            var v = new TextBlock { Text = value, FontSize = 12, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
            Grid.SetRow(l, row); Grid.SetRow(v, row); Grid.SetColumn(v, 1);
            grid.Children.Add(l); grid.Children.Add(v);
            row++;
        }
        return grid;
    }
}
