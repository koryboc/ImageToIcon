using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using ImageToIcon.Services;
using SkiaSharp;

namespace ImageToIcon.Ui;

public sealed class ShadowOutlineState
{
    public bool ShadowEnabled { get; set; }
    public Color ShadowColor { get; set; } = Color.FromArgb(160, 0, 0, 0);
    public double ShadowOffsetX { get; set; }
    public double ShadowOffsetY { get; set; } = 0.01;
    public double ShadowBlur { get; set; } = 0.02;

    public bool StrokeEnabled { get; set; }
    public Color StrokeColor { get; set; } = Color.FromArgb(255, 0, 0, 0);
    public double StrokeWidth { get; set; } = 0.01;

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

public static class ShadowOutlineFlyout
{
    public static void Show(
        Control anchor,
        ShadowOutlineState state,
        Action onChanged)
    {
        void Changed()
        {
            onChanged();
        }

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
            Changed();
        };
        strokeEnable.IsCheckedChanged += (_, _) =>
        {
            state.StrokeEnabled = strokeEnable.IsChecked == true;
            Changed();
        };

        AttachSlider(shadowOffX, shadowOffXLbl, shadowEnable, v =>
        {
            state.ShadowOffsetX = v;
            Changed();
        });
        AttachSlider(shadowOffY, shadowOffYLbl, shadowEnable, v =>
        {
            state.ShadowOffsetY = v;
            Changed();
        });
        AttachSlider(shadowBlur, shadowBlurLbl, shadowEnable, v =>
        {
            state.ShadowBlur = v;
            Changed();
        });
        AttachSlider(strokeWidth, strokeWidthLbl, strokeEnable, v =>
        {
            state.StrokeWidth = v;
            Changed();
        });

        var shadowGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("60,*,28"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto"),
            RowSpacing = 2, ColumnSpacing = 6
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
            ColumnDefinitions = new ColumnDefinitions("60,*,28"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            RowSpacing = 2, ColumnSpacing = 6,
            Margin = new Thickness(0, 6, 0, 0)
        };
        Grid.SetColumn(strokeEnable, 0);
        Grid.SetColumnSpan(strokeEnable, 2);
        Grid.SetRow(strokeEnable, 0);
        strokeGrid.Children.Add(strokeEnable);
        AddRow(strokeGrid, 1, "Color", strokeSwatch, null);
        AddRow(strokeGrid, 2, "Width", strokeWidth, strokeWidthLbl);

        var root = new StackPanel
        {
            Width = 240,
            Children = { shadowGrid, strokeGrid }
        };

        var flyout = new Flyout
        {
            Content = root,
            Placement = PlacementMode.Bottom,
            ShowMode = FlyoutShowMode.Transient
        };

        shadowSwatch.Click += async (_, _) =>
        {
            var host = TopLevel.GetTopLevel(anchor) as Window;
            if (host == null) return;
            var picked = await BackgroundColorDialog.ShowAsync(host, "Shadow Color", state.ShadowColor, false);
            if (picked == null) return;
            state.ShadowColor = picked.Value;
            SetSwatch(shadowSwatch, picked.Value);
            if (shadowEnable.IsChecked != true)
                shadowEnable.IsChecked = true;
            else
                Changed();
        };
        strokeSwatch.Click += async (_, _) =>
        {
            var host = TopLevel.GetTopLevel(anchor) as Window;
            if (host == null) return;
            var picked = await BackgroundColorDialog.ShowAsync(host, "Stroke Color", state.StrokeColor, false);
            if (picked == null) return;
            state.StrokeColor = picked.Value;
            SetSwatch(strokeSwatch, picked.Value);
            if (strokeEnable.IsChecked != true)
                strokeEnable.IsChecked = true;
            else
                Changed();
        };

        flyout.ShowAt(anchor);
    }

    private static void AttachSlider(Slider slider, TextBlock label, CheckBox enable, Action<double> apply)
    {
        slider.ValueChanged += (_, e) =>
        {
            label.Text = Fmt(e.NewValue);
            apply(e.NewValue / 100.0);
            if (enable.IsChecked != true)
                enable.IsChecked = true;
        };
    }

    private static Slider BuildPercentSlider(double min, double max, double normalized, out TextBlock label)
    {
        var s = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = normalized * 100.0,
            Classes = { "compact" },
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
            Width = 36,
            Height = 16,
            MinHeight = 0,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Content = new Rectangle { Fill = new SolidColorBrush(color), Width = 32, Height = 12 }
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
