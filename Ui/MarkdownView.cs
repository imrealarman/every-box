using System.Text.RegularExpressions;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace EveryStore;

/// <summary>
/// Shows README and release-note markdown as plain, readable blocks: headings, bullets, code and paragraphs.
/// Images, badges, HTML and link targets are dropped; this is a reading view, not a browser.
/// </summary>
public static class MarkdownView
{
    public static StackPanel Build(string? markdown, int maxLines = 400)
    {
        var panel = new StackPanel { Spacing = 6 };
        if (string.IsNullOrWhiteSpace(markdown))
        {
            panel.Children.Add(Text("Nothing here yet.", muted: true));
            return panel;
        }

        var paragraph = new List<string>();
        bool code = false;
        void Flush()
        {
            if (paragraph.Count == 0) return;
            panel.Children.Add(Text(string.Join(" ", paragraph)));
            paragraph.Clear();
        }

        foreach (var raw in markdown.Replace("\r", "").Split('\n').Take(maxLines))
        {
            string line = raw.TrimEnd();
            if (line.TrimStart().StartsWith("```")) { Flush(); code = !code; continue; }
            if (code) { panel.Children.Add(CodeLine(line)); continue; }

            string t = line.Trim();
            if (t.Length == 0 || Regex.IsMatch(t, @"^[-*_]{3,}$")) { Flush(); continue; }
            if (t.StartsWith("![") || t.StartsWith("[![")) continue; // images and badges

            var heading = Regex.Match(t, @"^(#{1,6})\s+(.*)$");
            if (heading.Success)
            {
                Flush();
                int level = heading.Groups[1].Length;
                var h = Text(Clean(heading.Groups[2].Value));
                h.FontSize = level == 1 ? 20 : level == 2 ? 16 : 13;
                h.FontWeight = FontWeights.SemiBold;
                h.Margin = new Thickness(0, level <= 2 ? 10 : 6, 0, 0);
                panel.Children.Add(h);
                continue;
            }

            var bullet = Regex.Match(line, @"^(\s*)([-*+]|\d+\.)\s+(.*)$");
            if (bullet.Success)
            {
                Flush();
                var b = Text("•  " + Clean(bullet.Groups[3].Value));
                b.Margin = new Thickness(8 + bullet.Groups[1].Length * 6, 0, 0, 0);
                panel.Children.Add(b);
                continue;
            }

            if (t.StartsWith('|')) { Flush(); if (!Regex.IsMatch(t, @"^[|\s:-]+$")) panel.Children.Add(CodeLine(t)); continue; }

            string cleaned = Clean(t);
            if (cleaned.Length > 0) paragraph.Add(cleaned);
        }
        Flush();
        return panel;
    }

    /// <summary>Removes markdown and HTML syntax from one line, keeping the words.</summary>
    public static string Clean(string s)
    {
        s = Regex.Replace(s, @"!\[[^\]]*\]\([^)]*\)", "");
        s = Regex.Replace(s, @"\[([^\]]*)\]\([^)]*\)", "$1");
        s = Regex.Replace(s, @"<[^>]+>", "");
        s = Regex.Replace(s, @"(\*\*|__|`)", "");
        s = Regex.Replace(s, @"(?<![\w*])\*(\S[^*]*)\*(?![\w*])", "$1");
        return s.Trim();
    }

    private static TextBlock Text(string text, bool muted = false)
    {
        var tb = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13, IsTextSelectionEnabled = true };
        if (muted) tb.Style = (Style)Application.Current.Resources["MutedText"];
        return tb;
    }

    private static TextBlock CodeLine(string line) => new()
    {
        Text = line.Length == 0 ? " " : line,
        FontFamily = new FontFamily("Cascadia Mono, Consolas"),
        FontSize = 12,
        IsTextSelectionEnabled = true,
        Margin = new Thickness(8, 0, 0, 0),
    };
}
