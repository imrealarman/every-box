using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace EveryStore;

/// <summary>
/// The bug report and feedback form. With a GitHub token in Settings the report is posted straight to the app's
/// issues; without one, GitHub's new-issue page opens with everything filled in.
/// </summary>
public static class ReportDialog
{
    /// <summary>Shows the form. Returns true when a report was posted from the store (so the list should reload).</summary>
    public static async Task<bool> ShowAsync(XamlRoot root, ElementTheme theme, StoreModel model, AppEntry entry, ReportKind kind)
    {
        bool bug = kind == ReportKind.Bug;
        bool canPost = !string.IsNullOrEmpty(model.Settings.Token);
        int rating = 0;

        var title = new TextBox { Header = bug ? "Summary" : "Headline", PlaceholderText = bug ? "What went wrong?" : "What would you tell the developer?", MaxLength = 120 };
        var details = new TextBox { Header = bug ? "What happened" : "Your feedback", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90, MaxHeight = 160 };
        var steps = new TextBox { Header = "Steps to reproduce (optional)", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 70, MaxHeight = 120 };
        var diagnostics = new CheckBox
        {
            Content = $"Include app version and Windows version ({Reports.Diagnostics(entry.Name, entry.InstalledVersion, StoreModel.StoreVersion)})",
            IsChecked = true,
        };
        var error = new TextBlock { Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.IndianRed), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        var how = new TextBlock
        {
            Style = (Style)Application.Current.Resources["MutedText"],
            TextWrapping = TextWrapping.Wrap,
            Text = canPost ? "This will be posted to the app's GitHub issues with your account."
                           : "GitHub will open with this filled in. You review it and press Submit there. Add a token in Settings to send from here.",
        };

        var form = new StackPanel { Spacing = 12, Width = 520 };
        form.Children.Add(title);
        if (!bug)
        {
            var stars = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            var label = new TextBlock { Text = "Rating", Style = (Style)Application.Current.Resources["PanelHeader"] };
            var buttons = new List<Button>();
            for (int i = 1; i <= 5; i++)
            {
                int value = i;
                var b = new Button { Content = "☆", FontSize = 22, Style = (Style)Application.Current.Resources["GhostButton"], Padding = new Thickness(6, 0, 6, 0) };
                b.Click += (_, _) =>
                {
                    rating = value;
                    for (int j = 0; j < 5; j++) buttons[j].Content = j < rating ? "★" : "☆";
                };
                buttons.Add(b);
                stars.Children.Add(b);
            }
            form.Children.Add(new StackPanel { Spacing = 2, Children = { label, stars } });
        }
        form.Children.Add(details);
        if (bug) form.Children.Add(steps);
        form.Children.Add(diagnostics);
        form.Children.Add(how);
        form.Children.Add(error);

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = theme,
            Title = bug ? $"Report a bug in {entry.Name}" : $"Send feedback on {entry.Name}",
            Content = new ScrollViewer { Content = form },
            PrimaryButtonText = canPost ? "Send" : "Continue on GitHub",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        bool posted = false;
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            error.Visibility = Visibility.Collapsed;
            if (title.Text.Trim().Length < 3 || details.Text.Trim().Length < 3)
            {
                args.Cancel = true;
                Show(error, "Please fill in both the summary and the details.");
                return;
            }
            if (!bug && rating == 0)
            {
                args.Cancel = true;
                Show(error, "Pick a star rating first.");
                return;
            }

            var report = new Report(kind, title.Text, details.Text, steps.Text, bug ? null : rating,
                diagnostics.IsChecked == true, entry.Name, entry.InstalledVersion, StoreModel.StoreVersion);

            if (!canPost)
            {
                await Launcher.LaunchUriAsync(new Uri(Reports.NewIssueUrl(entry.App.Repo, report)));
                return;
            }

            var deferral = args.GetDeferral();
            dialog.IsPrimaryButtonEnabled = false;
            try
            {
                await model.Github.CreateIssueAsync(entry.App.Repo, Reports.IssueTitle(report), Reports.Body(report), Reports.Labels(kind));
                posted = true;
            }
            catch (GitHubException ex)
            {
                args.Cancel = true;
                Show(error, ex.Message);
            }
            catch (HttpRequestException)
            {
                args.Cancel = true;
                Show(error, "Couldn't reach GitHub. Check your internet connection.");
            }
            finally
            {
                dialog.IsPrimaryButtonEnabled = true;
                deferral.Complete();
            }
        };

        await dialog.ShowAsync();
        return posted;
    }

    private static void Show(TextBlock block, string message)
    {
        block.Text = message;
        block.Visibility = Visibility.Visible;
    }
}
