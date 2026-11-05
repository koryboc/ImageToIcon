using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ImageToIcon.Models;
using ImageToIcon.Platform;
using ImageToIcon.Services;
using SkiaSharp;

namespace ImageToIcon.Ui;

public sealed class ShadowOutlineState
{
    public bool ShadowEnabled { get; set; }
    public Color ShadowColor { get; set; } = Color.FromArgb(160, 0, 0, 0);
    public double ShadowOffsetX { get; set; }
    public double ShadowOffsetY { get; set; } = 0.03;
    public double ShadowBlur { get; set; } = 0.04;

    public bool StrokeEnabled { get; set; }
    public Color StrokeColor { get; set; } = Color.FromArgb(255, 0, 0, 0);
    public double StrokeWidth { get; set; } = 0.02;

    public bool AnyEnabled => ShadowEnabled || StrokeEnabled;

    public ShadowSpec? Shadow => ShadowEnabled
        ? new ShadowSpec(new SKColor(ShadowColor.R, ShadowColor.G, ShadowColor.B, ShadowColor.A), ShadowOffsetX, ShadowOffsetY, ShadowBlur)
        : null;

    public StrokeSpec? Stroke => StrokeEnabled
        ? new StrokeSpec(new SKColor(StrokeColor.R, StrokeColor.G, StrokeColor.B, StrokeColor.A), StrokeWidth)
        : null;

    public ShadowOutlineState Clone()
    {
        return new ShadowOutlineState
        {
            ShadowEnabled = ShadowEnabled,
            ShadowColor = ShadowColor,
            ShadowOffsetX = ShadowOffsetX,
            ShadowOffsetY = ShadowOffsetY,
            ShadowBlur = ShadowBlur,
            StrokeEnabled = StrokeEnabled,
            StrokeColor = StrokeColor,
            StrokeWidth = StrokeWidth
        };
    }
}

public static class ShadowOutlineDialog
{
    public static async Task<ShadowOutlineState?> ShowAsync(
        Window owner,
        ShadowOutlineState current,
        Func<ShadowOutlineState, SKBitmap?>? renderPreview = null)
    {
        var state = current.Clone();
        ShadowOutlineState? result = null;

        var preview = new Image
        {
            Width = 192,
            Height = 192,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var previewBg = new Border
        {
            Width = 208,
            Height = 208,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
            Child = preview,
            Margin = new Thickness(0, 0, 0, 12)
        };

        var shadowEnable = new CheckBox { Content = "Shadow", IsChecked = state.ShadowEnabled, FontWeight = FontWeight.SemiBold };
        var shadowSwatch = BuildSwatch(state.ShadowColor);
        var shadowOffX = BuildPercentSlider(-20, 20, state.ShadowOffsetX, out var shadowOffXLbl);
        var shadowOffY = BuildPercentSlider(-20, 20, state.ShadowOffsetY, out var shadowOffYLbl);
        var shadowBlur = BuildPercentSlider(0, 20, state.ShadowBlur, out var shadowBlurLbl);

        var strokeEnable = new CheckBox { Content = "Stroke", IsChecked = state.StrokeEnabled, FontWeight = FontWeight.SemiBold };
        var strokeSwatch = BuildSwatch(state.StrokeColor);
        var strokeWidth = BuildPercentSlider(0, 10, state.StrokeWidth, out var strokeWidthLbl);

        shadowEnable.IsCheckedChanged += (_, _) =>
        {
            state.ShadowEnabled = shadowEnable.IsChecked == true;
            UpdatePreview();
        };
        strokeEnable.IsCheckedChanged += (_, _) =>
        {
            state.StrokeEnabled = strokeEnable.IsChecked == true;
            UpdatePreview();
        };
        shadowOffX.ValueChanged += (_, e) =>
        {
            state.ShadowOffsetX = e.NewValue / 100.0;
            shadowOffXLbl.Text = Fmt(e.NewValue);
            UpdatePreview();
        };
        shadowOffY.ValueChanged += (_, e) =>
        {
            state.ShadowOffsetY = e.NewValue / 100.0;
            shadowOffYLbl.Text = Fmt(e.NewValue);
            UpdatePreview();
        };
        shadowBlur.ValueChanged += (_, e) =>
        {
            state.ShadowBlur = e.NewValue / 100.0;
            shadowBlurLbl.Text = Fmt(e.NewValue);
            UpdatePreview();
        };
        strokeWidth.ValueChanged += (_, e) =>
        {
            state.StrokeWidth = e.NewValue / 100.0;
            strokeWidthLbl.Text = Fmt(e.NewValue);
            UpdatePreview();
        };

        Window? dlg = null;
        var dlg1 = dlg;
        shadowSwatch.Click += async (_, _) =>
        {
            if (dlg1 == null) return;
            var picked = await BackgroundColorDialog.ShowAsync(dlg1, "Shadow Color", state.ShadowColor, false);
            if (picked == null) return;
            state.ShadowColor = picked.Value;
            SetSwatch(shadowSwatch, picked.Value);
            UpdatePreview();
        };
        var dlg2 = dlg;
        strokeSwatch.Click += async (_, _) =>
        {
            if (dlg2 == null) return;
            var picked = await BackgroundColorDialog.ShowAsync(dlg2, "Stroke Color", state.StrokeColor, false);
            if (picked == null) return;
            state.StrokeColor = picked.Value;
            SetSwatch(strokeSwatch, picked.Value);
            UpdatePreview();
        };

        var shadowGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("80,*,32"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto"),
            RowSpacing = 6, ColumnSpacing = 8
        };
        Grid.SetColumn(shadowEnable, 0);
        Grid.SetColumnSpan(shadowEnable, 2);
        Grid.SetRow(shadowEnable, 0);
        shadowGrid.Children.Add(shadowEnable);
        AddRow(shadowGrid, 1, "Color", shadowSwatch, null);
        AddRow(shadowGrid, 2, "Offset X", shadowOffX, shadowOffXLbl);
        AddRow(shadowGrid, 3, "Offset Y", shadowOffY, shadowOffYLbl);
        AddRow(shadowGrid, 4, "Blur", shadowBlur, shadowBlurLbl);

        var strokeGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("80,*,32"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            RowSpacing = 6, ColumnSpacing = 8,
            Margin = new Thickness(0, 12, 0, 0)
        };
        Grid.SetColumn(strokeEnable, 0);
        Grid.SetColumnSpan(strokeEnable, 2);
        Grid.SetRow(strokeEnable, 0);
        strokeGrid.Children.Add(strokeEnable);
        AddRow(strokeGrid, 1, "Color", strokeSwatch, null);
        AddRow(strokeGrid, 2, "Width", strokeWidth, strokeWidthLbl);

        var okBtn = new Button { Content = "OK", Width = 80, IsDefault = true, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancelBtn = new Button { Content = "Cancel", Width = 80, IsCancel = true, HorizontalContentAlignment = HorizontalAlignment.Center };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { okBtn, cancelBtn }
        };

        var root = new StackPanel
        {
            Margin = new Thickness(16),
            Width = 340,
            Children = { previewBg, shadowGrid, strokeGrid, buttons }
        };

        dlg = new Window
        {
            Title = "Shadow & Stroke",
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
            result = state;
            dlg.Close();
        };
        cancelBtn.Click += (_, _) => dlg.Close();
        dlg.Opened += (_, _) =>
        {
            Win32Window.ApplyDarkTitlebar(dlg);
            UpdatePreview();
        };
        await dlg.ShowDialog(owner);
        if (preview.Source is Bitmap finalBmp)
            finalBmp.Dispose();
        return result;

        void UpdatePreview()
        {
            var bmp = renderPreview?.Invoke(state);
            if (bmp == null) return;
            var old = preview.Source as Bitmap;
            preview.Source = IconThumb.ToAvaloniaBitmap(bmp);
            bmp.Dispose();
            old?.Dispose();
        }
    }

    private static Slider BuildPercentSlider(double min, double max, double normalized, out TextBlock label)
    {
        var s = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = normalized * 100.0,
            VerticalAlignment = VerticalAlignment.Center
        };
        label = new TextBlock
        {
            Text = Fmt(s.Value),
            MinWidth = 28,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11
        };
        return s;
    }

    private static Button BuildSwatch(Color color)
    {
        return new Button
        {
            Width = 48,
            Height = 20,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Content = new Rectangle { Fill = new SolidColorBrush(color), Width = 44, Height = 16 }
        };
    }

    private static void SetSwatch(Button btn, Color color)
    {
        if (btn.Content is Rectangle r)
            r.Fill = new SolidColorBrush(color);
    }

    private static void AddRow(Grid grid, int row, string label, Control control, TextBlock? valueLbl)
    {
        var lbl = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, FontSize = 11 };
        Grid.SetColumn(lbl, 0);
        Grid.SetRow(lbl, row);
        Grid.SetColumn(control, 1);
        Grid.SetRow(control, row);
        grid.Children.Add(lbl);
        grid.Children.Add(control);
        if (valueLbl != null)
        {
            Grid.SetColumn(valueLbl, 2);
            Grid.SetRow(valueLbl, row);
            grid.Children.Add(valueLbl);
        }
    }

    private static string Fmt(double v)
    {
        return ((int)Math.Round(v)).ToString();
    }
}
