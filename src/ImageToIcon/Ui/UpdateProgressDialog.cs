using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ImageToIcon.Platform;
using ImageToIcon.Services;

namespace ImageToIcon.Ui;

public sealed class UpdateProgressDialog
{
    private readonly ProgressBar _bar;
    private readonly Button _closeBtn;
    private readonly Window _dlg;
    private readonly TextBlock _status;

    private UpdateProgressDialog(Window owner)
    {
        _bar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Height = 14,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _status = new TextBlock
        {
            Text = "Downloading update...",
            TextWrapping = TextWrapping.Wrap
        };
        _closeBtn = new Button
        {
            Content = "Close",
            Width = 80,
            IsEnabled = false,
            IsCancel = true,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };

        _dlg = new Window
        {
            Title = "Installing update",
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = TryBrush(owner, "AppWindowBrush"),
            Foreground = TryBrush(owner, "AppTextBrush"),
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 12,
                Children =
                {
                    _status,
                    _bar,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { _closeBtn }
                    }
                }
            }
        };
        _closeBtn.Click += (_, _) => _dlg.Close();
        _dlg.Opened += (_, _) => Win32Window.ApplyDarkTitlebar(_dlg);
    }

    public static async Task RunAsync(Window owner, SelfUpdateCoordinator coordinator)
    {
        var dialog = new UpdateProgressDialog(owner);
        var progress = new Progress<int>(dialog.ReportPercent);
        Exception? error = null;

        var work = Task.Run(async () =>
        {
            try
            {
                await coordinator.ApplyAsync(progress);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        _ = work.ContinueWith(_ =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (error != null)
                    dialog.ReportError(error);
                else
                    dialog.ReportComplete();
            });
        });

        await dialog._dlg.ShowDialog(owner);
    }

    private void ReportPercent(int percent)
    {
        _bar.Value = Math.Clamp(percent, 0, 100);
        _status.Text = $"Downloading update... {_bar.Value:0}%";
    }

    private void ReportError(Exception ex)
    {
        _bar.Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B));
        _status.Text = $"Update failed: {ex.Message}";
        _closeBtn.IsEnabled = true;
    }

    private void ReportComplete()
    {
        _bar.Value = 100;
        _status.Text = "Restarting to apply update...";
        _closeBtn.IsEnabled = true;
    }

    private static IBrush? TryBrush(Window owner, string key)
    {
        return owner.TryFindResource(key, owner.ActualThemeVariant, out var value) && value is IBrush brush
            ? brush
            : null;
    }
}