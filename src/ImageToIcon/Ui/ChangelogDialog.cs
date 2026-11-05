using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ImageToIcon.Platform;

namespace ImageToIcon.Ui;

public static class ChangelogDialog
{
    public static async Task<bool> ShowAsync(Window owner, Version version, string? body)
    {
        var highlights = ExtractHighlights(body);
        var listPanel = new StackPanel
        {
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 16, 0)
        };
        if (highlights.Length == 0)
        {
            listPanel.Children.Add(new TextBlock
            {
                Text = "No highlights listed for this release.",
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                FontStyle = FontStyle.Italic,
                FontWeight = FontWeight.Normal
            });
        }
        else
        {
            foreach (var item in highlights)
            {
                listPanel.Children.Add(new TextBlock
                {
                    Text = "\u2022 " + item,
                    TextWrapping = TextWrapping.Wrap,
                    FontWeight = FontWeight.Normal
                });
            }
        }

        var installBtn = new Button
        {
            Content = "Install Update",
            Width = 120,
            IsDefault = true,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        var cancelBtn = new Button
        {
            Content = "Cancel",
            Width = 80,
            IsCancel = true,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };

        var confirmed = false;
        var dlg = new Window
        {
            Title = "Update Available",
            Width = 480,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = TryBrush(owner, "AppWindowBrush"),
            Foreground = TryBrush(owner, "AppTextBrush"),
            Content = new StackPanel
            {
                Margin = new Thickness(20, 20, 0, 20),
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"Version {version}",
                        FontSize = 16,
                        FontWeight = FontWeight.SemiBold,
                        Margin = new Thickness(0, 0, 20, 0)
                    },
                    new TextBlock
                    {
                        Text = "Version Highlights",
                        FontWeight = FontWeight.SemiBold,
                        Margin = new Thickness(0, 0, 20, 0)
                    },
                    new ScrollViewer
                    {
                        MaxHeight = 260,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        Padding = new Thickness(0, 0, 4, 0),
                        Content = listPanel
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Margin = new Thickness(0, 0, 20, 0),
                        Children = { installBtn, cancelBtn }
                    }
                }
            }
        };

        installBtn.Click += (_, _) =>
        {
            confirmed = true;
            dlg.Close();
        };
        cancelBtn.Click += (_, _) => dlg.Close();

        dlg.Opened += (_, _) => Win32Window.ApplyDarkTitlebar(dlg);
        await dlg.ShowDialog(owner);
        return confirmed;
    }

    private static IBrush? TryBrush(Window owner, string key)
    {
        return owner.TryFindResource(key, owner.ActualThemeVariant, out var value) && value is IBrush brush
            ? brush
            : null;
    }

    private static string[] ExtractHighlights(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return [];

        var lines = body.Replace("\r\n", "\n").Split('\n');
        var items = new List<string>();
        var inSection = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("###", StringComparison.Ordinal))
            {
                if (inSection)
                    break;
                if (line.Trim().Equals("### Version Highlights", StringComparison.OrdinalIgnoreCase))
                    inSection = true;
                continue;
            }

            if (!inSection)
                continue;

            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
                items.Add(trimmed[2..].Trim());
        }

        return items.ToArray();
    }
}