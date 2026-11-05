using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ImageToIcon.Platform;

namespace ImageToIcon.Ui;

public static class BackgroundColorDialog
{
    public static async Task<Color?> ShowAsync(Window owner, Color initial)
    {
        Color? picked = null;

        var view = new ColorView
        {
            Color = initial,
            IsAlphaEnabled = true,
            IsColorPaletteVisible = true,
            IsColorPreviewVisible = true,
            IsAccentColorsVisible = false,
            Width = 360,
            Height = 420
        };

        var okBtn = new Button
        {
            Content = "OK",
            Width = 80,
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

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0),
            Children = { okBtn, cancelBtn }
        };

        var root = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            Children = { view, buttons }
        };

        var dlg = new Window
        {
            Title = "Background color",
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = owner.Background,
            Foreground = owner.Foreground,
            Content = root
        };

        okBtn.Click += (_, _) =>
        {
            picked = view.Color.A == 0 ? null : view.Color;
            dlg.Close();
        };
        cancelBtn.Click += (_, _) => dlg.Close();

        dlg.Opened += (_, _) => Win32Window.ApplyDarkTitlebar(dlg);
        await dlg.ShowDialog(owner);
        return picked;
    }
}
