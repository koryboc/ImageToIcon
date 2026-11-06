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
    public Color ShadowColor { get; set; } = Color.FromArgb(200, 0, 0, 0);
    public double ShadowOffsetX { get; set; }
    public double ShadowOffsetY { get; set; } = 0.02;
    public double ShadowBlur { get; set; } = 0.01;

    public bool StrokeEnabled { get; set; }
    public Color StrokeColor { get; set; } = Color.FromArgb(128, 0, 0, 0);
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
        var defaults = new ShadowOutlineState();

        static int Pct(double v)
        {
            return (int)Math.Round(v * 100.0);
        }

        bool ShadowAtDefault()
        {
            return state.ShadowColor == defaults.ShadowColor
                   && Pct(state.ShadowOffsetX) == Pct(defaults.ShadowOffsetX)
                   && Pct(state.ShadowOffsetY) == Pct(defaults.ShadowOffsetY)
                   && Pct(state.ShadowBlur) == Pct(defaults.ShadowBlur);
        }

        bool StrokeAtDefault()
        {
            return state.StrokeColor == defaults.StrokeColor
                   && Pct(state.StrokeWidth) == Pct(defaults.StrokeWidth);
        }

        var shadowReset = BuildResetButton();
        var strokeReset = BuildResetButton();
        shadowReset.IsEnabled = !ShadowAtDefault();
        strokeReset.IsEnabled = !StrokeAtDefault();

        void Changed()
        {
            shadowReset.IsEnabled = !ShadowAtDefault();
            strokeReset.IsEnabled = !StrokeAtDefault();
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

        shadowReset.Click += (_, _) =>
        {
            var d = new ShadowOutlineState();
            state.ShadowColor = d.ShadowColor;
            state.ShadowOffsetX = d.ShadowOffsetX;
            state.ShadowOffsetY = d.ShadowOffsetY;
            state.ShadowBlur = d.ShadowBlur;
            SetSwatch(shadowSwatch, state.ShadowColor);
            shadowOffX.Value = state.ShadowOffsetX * 100.0;
            shadowOffY.Value = state.ShadowOffsetY * 100.0;
            shadowBlur.Value = state.ShadowBlur * 100.0;
            Changed();
        };
        strokeReset.Click += (_, _) =>
        {
            var d = new ShadowOutlineState();
            state.StrokeColor = d.StrokeColor;
            state.StrokeWidth = d.StrokeWidth;
            SetSwatch(strokeSwatch, state.StrokeColor);
            strokeWidth.Value = state.StrokeWidth * 100.0;
            Changed();
        };

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
        Grid.SetColumn(shadowReset, 2);
        Grid.SetRow(shadowReset, 0);
        shadowGrid.Children.Add(shadowReset);
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
        Grid.SetColumn(strokeReset, 2);
        Grid.SetRow(strokeReset, 0);
        strokeGrid.Children.Add(strokeReset);
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
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            ShowMode = FlyoutShowMode.Transient
        };

        async Task PickColor(Button swatch, string title, Color current, Action<Color> commit)
        {
            if (TopLevel.GetTopLevel(anchor) is not Window host)
                return;
            var picked = await BackgroundColorDialog.ShowAsync(host, title, current, false);
            if (picked != null)
            {
                commit(picked.Value);
                SetSwatch(swatch, picked.Value);
            }

            flyout.ShowAt(anchor);
        }

        shadowSwatch.Click += async (_, _) =>
        {
            await PickColor(shadowSwatch, "Shadow Color", state.ShadowColor, c =>
            {
                state.ShadowColor = c;
                if (shadowEnable.IsChecked != true)
                    shadowEnable.IsChecked = true;
                else
                    Changed();
            });
        };
        strokeSwatch.Click += async (_, _) =>
        {
            await PickColor(strokeSwatch, "Stroke Color", state.StrokeColor, c =>
            {
                state.StrokeColor = c;
                if (strokeEnable.IsChecked != true)
                    strokeEnable.IsChecked = true;
                else
                    Changed();
            });
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

    private static Button BuildResetButton()
    {
        var btn = new Button
        {
            Content = "\u21BA",
            FontSize = 14,
            Width = 22,
            Height = 18,
            MinHeight = 0,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(btn, "Reset to defaults");
        return btn;
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
