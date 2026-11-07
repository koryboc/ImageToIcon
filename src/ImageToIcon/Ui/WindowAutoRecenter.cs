using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace ImageToIcon.Ui;

public static class WindowAutoRecenter
{
    private const int ScreenMargin = 48;

    public static void Attach(Window window)
    {
        var last = default(Size);
        var armed = false;

        window.Opened += (_, _) =>
        {
            ApplyMaxBounds(window);
            last = window.ClientSize;
            armed = true;
            Dispatcher.UIThread.Post(() => ClampPosition(window), DispatcherPriority.Background);
        };

        window.PropertyChanged += (_, e) =>
        {
            if (!armed || e.Property != TopLevel.ClientSizeProperty)
                return;

            var current = window.ClientSize;
            var dx = (int)((current.Width - last.Width) / 2);
            var dy = (int)((current.Height - last.Height) / 2);
            last = current;

            if (dx != 0 || dy != 0)
                window.Position = new PixelPoint(window.Position.X - dx, window.Position.Y - dy);

            ClampPosition(window);
        };
    }

    private static void ApplyMaxBounds(Window window)
    {
        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
        if (screen == null)
            return;
        var scale = screen.Scaling;
        var wa = screen.WorkingArea;
        var maxW = wa.Width / scale - 2 * ScreenMargin;
        var maxH = wa.Height / scale - 2 * ScreenMargin;
        if (maxW > 0)
            window.MaxWidth = maxW;
        if (maxH > 0)
            window.MaxHeight = maxH;
    }

    private static void ClampPosition(Window window)
    {
        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
        if (screen == null)
            return;
        var scale = screen.Scaling;
        var wa = screen.WorkingArea;
        var margin = (int)Math.Round(ScreenMargin * scale);
        var winW = (int)Math.Round(window.ClientSize.Width * scale);
        var winH = (int)Math.Round(window.ClientSize.Height * scale);
        var minX = wa.X + margin;
        var minY = wa.Y + margin;
        var maxX = Math.Max(minX, wa.X + wa.Width - winW - margin);
        var maxY = Math.Max(minY, wa.Y + wa.Height - winH - margin);
        var pos = window.Position;
        var newX = Math.Clamp(pos.X, minX, maxX);
        var newY = Math.Clamp(pos.Y, minY, maxY);
        if (newX != pos.X || newY != pos.Y)
            window.Position = new PixelPoint(newX, newY);
    }
}
