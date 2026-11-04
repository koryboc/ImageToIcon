using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;

namespace ImageToIcon.Platform;

public static partial class Win32Window
{
    public static void ApplyDarkTitlebar(Window window)
    {
        var isDark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
        ApplyDarkTitlebar(window, isDark);
    }

    public static void ApplyDarkTitlebar(Window window, bool dark)
    {
        if (!OperatingSystem.IsWindows())
            return;

        // Must be deferred: calling these APIs synchronously in OnOpened re-enters
        // Avalonia's WndProc via WM_NCPAINT and causes a crash.
        Dispatcher.UIThread.Post(() =>
        {
            var hwnd = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd == IntPtr.Zero)
                return;

            var value = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, 0x14 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref value, sizeof(int));

            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
                return;

            SendMessage(hwnd, 0x0086 /* WM_NCACTIVATE */, IntPtr.Zero, IntPtr.Zero);
            SendMessage(hwnd, 0x0086 /* WM_NCACTIVATE */, new IntPtr(1), IntPtr.Zero);
        }, DispatcherPriority.Background);
    }

    [LibraryImport("dwmapi.dll")]
    private static partial void DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageA")]
    private static partial void SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}