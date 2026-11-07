using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ImageToIcon.Models;
using ImageToIcon.Platform;
using ImageToIcon.Services;
using ImageToIcon.Ui;
using SkiaSharp;
using Color = Avalonia.Media.Color;
using Point = Avalonia.Point;

namespace ImageToIcon.Views;

public enum IconFormat
{
    Ico,
    Icns,
    Hicolor
}

public partial class MainWindow : Window
{
    private const double PanThresholdPx = 4;
    private readonly Button? _adjustButtonCtrl;
    private readonly Button? _bgColorToggleCtrl;
    private readonly Canvas? _cropIconCtrl;
    private readonly CancellationTokenSource _cts = new();
    private readonly Canvas? _fitIconCtrl;
    private readonly Button? _fitModeButtonCtrl;
    private readonly ObservableCollection<SizeToggle> _hicolorToggles = [];
    private readonly ObservableCollection<SizeToggle> _icnsToggles = [];
    private readonly Slider? _maskSliderCtrl;
    private readonly TextBlock? _maskValueCtrl;
    private readonly Slider? _paddingSliderCtrl;
    private readonly TextBlock? _paddingValueCtrl;
    private readonly SelfUpdateCoordinator _selfUpdate;
    private readonly Settings _settings;
    private readonly ObservableCollection<SizeToggle> _sizeToggles = [];
    private readonly ItemsControl? _smallPanel;
    private readonly ObservableCollection<IconThumb> _smallThumbs = [];
    private readonly Canvas? _stretchIconCtrl;
    private readonly ObservableCollection<IconThumb> _topThumbs = [];
    private readonly Button _updateBtn;
    private SKColor? _backgroundColor;
    private CancellationTokenSource? _bgDebounceCts;
    private bool _cornerEnabled;
    private double _cropOffsetX;
    private double _cropOffsetY;
    private double _cropZoom = 1;
    private bool _debugPreview;
    private FitMode _fitMode;
    private IconThumb? _focusedThumb;
    private IconFormat _format;
    private ShadowOutlineState _fxState = new();
    private bool _iconIsDebug;
    private Color _lastBackgroundColor = Colors.White;
    private CancellationTokenSource? _maskDebounceCts;
    private double _maskRadius;
    private double _padding;
    private bool _paddingEnabled;
    private bool _panActive;
    private double _panStartCropX;
    private double _panStartCropY;
    private IconThumb? _pressedThumb;
    private bool _pressedThumbWasRight;
    private bool _pressMovedBeyondThreshold;
    private Point _pressPoint;
    private Task _rebuildChain = Task.CompletedTask;
    private CancellationTokenSource? _rebuildCts;
    private SKColor? _savedBackgroundColor;
    private double _savedCropOffsetX;
    private double _savedCropOffsetY;
    private double _savedCropZoom = 1;
    private FitMode _savedFitMode;
    private IconThumb? _savedFocusedThumb;
    private ShadowOutlineState? _savedFxState;
    private double _savedMaskRadius;
    private double _savedPadding;

    private bool _sliderDragging;
    private bool _sourceHasTransparency;
    private SKBitmap? _sourceImage;
    private string? _sourceName;
    private bool _suppressTopFrameSync;
    private TrayIcon? _trayIcon;

    private CancellationTokenSource? _zoomDebounceCts;

    public MainWindow() : this(null)
    {
    }

    public MainWindow(string? startupFile)
    {
        InitializeComponent();

        if (OperatingSystem.IsWindows())
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

        WindowAutoRecenter.Attach(this);

        _settings = Settings.Load();
        _selfUpdate = new SelfUpdateCoordinator(_settings, _cts.Token);
        _fxState.ShadowColor = Color.FromUInt32(_settings.ShadowColor);
        _fxState.ShadowOffsetX = _settings.ShadowOffsetX;
        _fxState.ShadowOffsetY = _settings.ShadowOffsetY;
        _fxState.ShadowBlur = _settings.ShadowBlur;
        _fxState.StrokeColor = Color.FromUInt32(_settings.StrokeColor);
        _fxState.StrokeWidth = _settings.StrokeWidth;
        _maskRadius = _settings.CornerRadius;
        _padding = _settings.Padding;

        var openBtn = this.FindControl<Button>("OpenBtn")!;
        var saveBtn = this.FindControl<Button>("SaveBtn")!;
        var addSizeBtn = this.FindControl<Button>("AddSizeBtn")!;
        _updateBtn = this.FindControl<Button>("UpdateBtn")!;
        var topPanel = this.FindControl<ItemsControl>("TopThumbs")!;
        _smallPanel = this.FindControl<ItemsControl>("SmallThumbs")!;
        var toggles = this.FindControl<ItemsControl>("SizeToggles")!;
        var borderToggle = this.FindControl<ToggleButton>("BorderToggle")!;
        borderToggle.IsChecked = _settings.ShowThumbBorders;
        var bgColorToggle = this.FindControl<Button>("BgColorToggle")!;
        _bgColorToggleCtrl = bgColorToggle;
        bgColorToggle.Click += (_, _) => ShowBackgroundColorFlyout();
        var fxButton = this.FindControl<Button>("FxButton")!;
        fxButton.Click += (_, _) => ShadowOutlineFlyout.Show(fxButton, _fxState, DebouncedBgRebuild);
        var debugToggle = this.FindControl<ToggleButton>("DebugToggle")!;
        debugToggle.Click += (_, _) =>
        {
            var next = debugToggle.IsChecked == true;
            switch (next)
            {
                case true when !_debugPreview:
                {
                    _savedFocusedThumb = _focusedThumb;
                    SetFocus(null);
                    _savedMaskRadius = _maskRadius;
                    _savedPadding = _padding;
                    _savedFitMode = _fitMode;
                    _savedCropOffsetX = _cropOffsetX;
                    _savedCropOffsetY = _cropOffsetY;
                    _savedCropZoom = _cropZoom;
                    _savedBackgroundColor = _backgroundColor;
                    _savedFxState = _fxState.Clone();
                    _backgroundColor = null;
                    _fxState = new ShadowOutlineState();
                    _bgColorToggleCtrl?.IsEnabled = false;

                    fxButton.IsEnabled = false;
                    break;
                }
                case false when _debugPreview:
                {
                    _maskRadius = _savedMaskRadius;
                    _padding = _savedPadding;
                    _fitMode = _savedFitMode;
                    _cropOffsetX = _savedCropOffsetX;
                    _cropOffsetY = _savedCropOffsetY;
                    _cropZoom = _savedCropZoom;
                    _backgroundColor = _savedBackgroundColor;
                    _fxState = _savedFxState ?? new ShadowOutlineState();
                    _savedFxState = null;
                    _bgColorToggleCtrl?.IsEnabled = true;

                    fxButton.IsEnabled = true;
                    _maskSliderCtrl?.Value = _maskRadius * 100;
                    _maskValueCtrl?.Text = ((int)Math.Round(_maskRadius * 100)).ToString();
                    _paddingSliderCtrl?.Value = _padding * 100;
                    _paddingValueCtrl?.Text = FormatPadding(_padding * 100);
                    UpdateFitModeLabel();
                    if (_savedFocusedThumb != null
                        && (_topThumbs.Contains(_savedFocusedThumb) || _smallThumbs.Contains(_savedFocusedThumb)))
                        SetFocus(_savedFocusedThumb);
                    _savedFocusedThumb = null;
                    break;
                }
            }

            _debugPreview = next;
            openBtn.IsEnabled = !_debugPreview;
            UpdateFitModeAvailability();
            RebuildThumbs(true);
        };

        var cornerEnable = this.FindControl<CheckBox>("CornerEnable")!;
        var paddingEnable = this.FindControl<CheckBox>("PaddingEnable")!;
        cornerEnable.IsChecked = _cornerEnabled;
        paddingEnable.IsChecked = _paddingEnabled;
        var maskSlider = this.FindControl<Slider>("MaskSlider")!;
        var maskValue = this.FindControl<TextBlock>("MaskValue")!;
        _maskSliderCtrl = maskSlider;
        _maskValueCtrl = maskValue;
        maskSlider.Value = _maskRadius * 100;
        maskValue.Text = ((int)Math.Round(_maskRadius * 100)).ToString();
        cornerEnable.IsCheckedChanged += (_, _) =>
        {
            _cornerEnabled = cornerEnable.IsChecked == true;
            UpdateBgColorAvailability();
            DebouncedRebuild();
        };
        paddingEnable.IsCheckedChanged += (_, _) =>
        {
            _paddingEnabled = paddingEnable.IsChecked == true;
            UpdateBgColorAvailability();
            DebouncedRebuild();
        };
        maskSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty)
                return;
            var v = maskSlider.Value / 100.0;
            maskValue.Text = ((int)Math.Round(maskSlider.Value)).ToString();
            var current = _focusedThumb != null ? _focusedThumb.MaskRadiusOverride ?? _maskRadius : _maskRadius;
            if (Math.Abs(v - current) < 0.0001)
                return;
            if (_focusedThumb != null)
            {
                _focusedThumb.MaskRadiusOverride = v;
                _focusedThumb.NotifyOverridesChanged();
            }
            else
            {
                _maskRadius = v;
            }

            if (cornerEnable.IsChecked != true)
                cornerEnable.IsChecked = true;
            UpdateBgColorAvailability();
            DebouncedRebuild();
        };

        var paddingSlider = this.FindControl<Slider>("PaddingSlider")!;
        var paddingValue = this.FindControl<TextBlock>("PaddingValue")!;
        _paddingSliderCtrl = paddingSlider;
        _paddingValueCtrl = paddingValue;
        paddingSlider.Value = _padding * 100;
        paddingValue.Text = FormatPadding(_padding * 100);
        paddingSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty)
                return;
            var v = paddingSlider.Value / 100.0;
            paddingValue.Text = FormatPadding(paddingSlider.Value);
            var current = _focusedThumb != null ? _focusedThumb.PaddingOverride ?? _padding : _padding;
            if (Math.Abs(v - current) < 0.0001)
                return;
            if (_focusedThumb != null)
            {
                _focusedThumb.PaddingOverride = v;
                _focusedThumb.NotifyOverridesChanged();
            }
            else
            {
                _padding = v;
            }

            if (paddingEnable.IsChecked != true)
                paddingEnable.IsChecked = true;
            UpdateBgColorAvailability();
            DebouncedRebuild();
        };

        var cornerReset = this.FindControl<Button>("CornerReset")!;
        var paddingReset = this.FindControl<Button>("PaddingReset")!;
        var cornerDefault = new Settings().CornerRadius * 100;
        var paddingDefault = new Settings().Padding * 100;
        cornerReset.IsEnabled = (int)Math.Round(maskSlider.Value) != (int)Math.Round(cornerDefault);
        paddingReset.IsEnabled = Math.Abs(paddingSlider.Value - paddingDefault) >= 0.05;
        cornerReset.Click += (_, _) => maskSlider.Value = cornerDefault;
        paddingReset.Click += (_, _) => paddingSlider.Value = paddingDefault;
        maskSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty)
                cornerReset.IsEnabled = (int)Math.Round(maskSlider.Value) != (int)Math.Round(cornerDefault);
        };
        paddingSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty)
                paddingReset.IsEnabled = Math.Abs(paddingSlider.Value - paddingDefault) >= 0.05;
        };

        AttachSliderDragTracking(maskSlider);
        AttachSliderDragTracking(paddingSlider);

        var fitModeButton = this.FindControl<Button>("FitModeButton")!;
        _fitIconCtrl = this.FindControl<Canvas>("FitIcon");
        _stretchIconCtrl = this.FindControl<Canvas>("StretchIcon");
        _cropIconCtrl = this.FindControl<Canvas>("CropIcon");
        _fitModeButtonCtrl = fitModeButton;
        _adjustButtonCtrl = this.FindControl<Button>("AdjustButton");
        UpdateFitModeLabel();
        fitModeButton.Click += (_, _) =>
        {
            var current = _focusedThumb != null ? _focusedThumb.FitModeOverride ?? _fitMode : _fitMode;
            var next = current switch
            {
                FitMode.Fit => FitMode.Stretch,
                FitMode.Stretch => FitMode.Crop,
                _ => FitMode.Fit
            };
            if (_focusedThumb != null)
            {
                _focusedThumb.FitModeOverride = next;
                _focusedThumb.NotifyOverridesChanged();
            }
            else
            {
                _fitMode = next;
            }

            UpdateFitModeLabel();
            RebuildThumbs(true);
        };

        paddingSlider.AddHandler(PointerPressedEvent, (_, _) => SetPaddingDragHighlight(true), RoutingStrategies.Tunnel);
        paddingSlider.AddHandler(PointerReleasedEvent, (_, _) => SetPaddingDragHighlight(false), RoutingStrategies.Tunnel);
        paddingSlider.AddHandler(PointerCaptureLostEvent, (_, _) => SetPaddingDragHighlight(false), RoutingStrategies.Tunnel);

        topPanel.ItemsSource = _topThumbs;
        _smallPanel.ItemsSource = _smallThumbs;
        toggles.ItemsSource = _sizeToggles;

        var selected = new HashSet<int>(_settings.SelectedSizes);
        var defaults = new HashSet<int>(IconFactory.DefaultSizes);
        var union = new SortedSet<int>(Comparer<int>.Create((a, b) => b.CompareTo(a)));
        foreach (var s in IconFactory.DefaultSizes)
            union.Add(s);
        foreach (var s in _settings.CustomSizes)
            union.Add(s);
        foreach (var toggle in union.Select(size => new SizeToggle(size, selected.Contains(size), !defaults.Contains(size))))
        {
            toggle.PropertyChanged += OnTogglePropertyChanged;
            _sizeToggles.Add(toggle);
        }

        // Persisted state may have multiple >=256 sizes checked; keep only
        // the largest to match the runtime radio-style rule.
        var topKeep = _sizeToggles
                      .Where(t => t is { Size: >= 256, IsChecked: true })
                      .MaxBy(t => t.Size);
        if (topKeep != null)
            EnforceTopFrameExclusivity(topKeep);

        var icnsSelected = new HashSet<int>(_settings.IcnsSelectedSizes);
        foreach (var size in Settings.DefaultIcnsSizes)
        {
            var t = new SizeToggle(size, icnsSelected.Contains(size));
            t.PropertyChanged += OnTogglePropertyChanged;
            _icnsToggles.Add(t);
        }

        var hicolorSelected = new HashSet<int>(_settings.HicolorSelectedSizes);
        foreach (var size in IconFactory.DefaultHicolorSizes)
        {
            var t = new SizeToggle(size, hicolorSelected.Contains(size));
            t.PropertyChanged += OnTogglePropertyChanged;
            _hicolorToggles.Add(t);
        }

        var formatIco = this.FindControl<ToggleButton>("FormatIco")!;
        var formatIcns = this.FindControl<ToggleButton>("FormatIcns")!;
        var formatHicolor = this.FindControl<ToggleButton>("FormatHicolor")!;

        void SelectFormat(IconFormat f)
        {
            formatIco.IsChecked = f == IconFormat.Ico;
            formatIcns.IsChecked = f == IconFormat.Icns;
            formatHicolor.IsChecked = f == IconFormat.Hicolor;
            addSizeBtn.IsVisible = f == IconFormat.Ico;
            if (_format == f)
                return;
            _format = f;
            toggles.ItemsSource = ActiveToggles;
            UpdateAvailability();
            RebuildThumbs(true);
        }

        formatIco.Click += (_, _) => SelectFormat(IconFormat.Ico);
        formatIcns.Click += (_, _) => SelectFormat(IconFormat.Icns);
        formatHicolor.Click += (_, _) => SelectFormat(IconFormat.Hicolor);

        if (_settings.HicolorFormat)
            SelectFormat(IconFormat.Hicolor);
        else if (_settings.IcnsFormat)
            SelectFormat(IconFormat.Icns);
        else
            SelectFormat(IconFormat.Ico);

        openBtn.Click += async (_, _) => await OpenImageAsync();
        saveBtn.Click += async (_, _) => await SaveIconAsync();
        addSizeBtn.Click += async (_, _) => await AddCustomSizeAsync();
        _updateBtn.Click += async (_, _) => await OnUpdateClickAsync();

        // Show cached pending update immediately, then check in background.
        RefreshUpdateButton();
        Opened += (_, _) =>
        {
            Win32Window.ApplyDarkTitlebar(this);
            _ = CheckForUpdateAsync();
        };

        if (Application.Current is { } app)
            app.ActualThemeVariantChanged += (_, _) => Win32Window.ApplyDarkTitlebar(this);

        AddHandler(DragDrop.DropEvent, OnWindowDrop);
        AddHandler(DragDrop.DragOverEvent, OnWindowDragOver);

        Closing += (_, _) =>
        {
            _settings.SelectedSizes = _sizeToggles.Where(t => t.Size >= 256 ? t.IsUserChecked : t.IsChecked).Select(t => t.Size).ToArray();
            _settings.CustomSizes = _sizeToggles.Where(t => t.IsCustom).Select(t => t.Size).ToArray();
            _settings.IcnsFormat = _format == IconFormat.Icns;
            _settings.HicolorFormat = _format == IconFormat.Hicolor;
            _settings.HicolorSelectedSizes = _hicolorToggles.Where(t => t.IsChecked).Select(t => t.Size).ToArray();
            _settings.IcnsSelectedSizes = _icnsToggles.Where(t => t.IsChecked).Select(t => t.Size).ToArray();
            _settings.ShowThumbBorders = borderToggle.IsChecked == true;
            var fx = _savedFxState ?? _fxState;
            _settings.ShadowColor = fx.ShadowColor.ToUInt32();
            _settings.ShadowOffsetX = fx.ShadowOffsetX;
            _settings.ShadowOffsetY = fx.ShadowOffsetY;
            _settings.ShadowBlur = fx.ShadowBlur;
            _settings.StrokeColor = fx.StrokeColor.ToUInt32();
            _settings.StrokeWidth = fx.StrokeWidth;
            _settings.CornerRadius = _debugPreview ? _savedMaskRadius : _maskRadius;
            _settings.Padding = _debugPreview ? _savedPadding : _padding;
            _settings.Save();
            _cts.Cancel();
            _rebuildCts?.Cancel();
            _trayIcon?.IsVisible = false;
        };

        if (startupFile != null)
            LoadFromPath(startupFile);
        else
            LoadDefaultSymbol();
    }

    private ObservableCollection<SizeToggle> ActiveToggles => _format switch
    {
        IconFormat.Icns => _icnsToggles,
        IconFormat.Hicolor => _hicolorToggles,
        _ => _sizeToggles
    };

    private double EffectiveMask => _cornerEnabled ? _maskRadius : 0;
    private double EffectivePadding => _paddingEnabled ? _padding : 0;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void LoadDefaultSymbol()
    {
        try
        {
            var uri = new Uri("avares://ImageToIcon/Assets/Symbol.svg");
            using var stream = AssetLoader.Open(uri);
            _sourceImage = SvgLoader.TryLoad(stream);
            if (_sourceImage != null)
            {
                _sourceName = "icon";
                _sourceHasTransparency = IconFactory.HasTransparency(_sourceImage);
                UpdateAvailability();
            }
        }
        catch
        {
            // ignored
        }

        RebuildThumbs(true);
    }

    private async void LoadFromPath(string path)
    {
        try
        {
            var img = ImageLoader.TryLoad(path);
            if (img == null)
                return;
            await CancelAndAwaitRebuildAsync();
            _sourceImage?.Dispose();
            _sourceImage = img;
            _sourceName = Path.GetFileNameWithoutExtension(path);
            _sourceHasTransparency = IconFactory.HasTransparency(img);
            UpdateAvailability();
            RebuildThumbs(true);
        }
        catch
        {
            // ignored
        }
    }

    private async Task CancelAndAwaitRebuildAsync()
    {
        if (_rebuildCts != null)
            await _rebuildCts.CancelAsync();
        try
        {
            await _rebuildChain;
        }
        catch
        {
            // ignored — cancellation
        }
    }

    private void UpdateAvailability()
    {
        if (_sourceImage == null)
            return;
        var maxDim = Math.Max(_sourceImage.Width, _sourceImage.Height);
        foreach (var t in _sizeToggles)
            t.IsAvailable = t.Size <= maxDim;
        foreach (var t in _icnsToggles)
            t.IsAvailable = t.Size <= maxDim;
        foreach (var t in _hicolorToggles)
            t.IsAvailable = t.Size <= maxDim;
        PromoteLargestAvailableTopFrame();
        UpdateFitModeAvailability();
        UpdateBgColorAvailability();
    }

    private void OnTogglePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SizeToggle.IsChecked))
            return;

        if (_format == IconFormat.Ico && sender is SizeToggle { Size: >= 256 } top && !_suppressTopFrameSync)
            HandleTopToggleUserClick(top);

        RebuildThumbs();
    }

    private void HandleTopToggleUserClick(SizeToggle clicked)
    {
        var becameChecked = clicked.IsChecked;
        _suppressTopFrameSync = true;
        try
        {
            if (becameChecked)
            {
                foreach (var other in _sizeToggles)
                {
                    if (other == clicked || other.Size < 256) continue;
                    other.IsChecked = false;
                    other.IsUserChecked = false;
                    other.IsAutoPromoted = false;
                }

                clicked.IsUserChecked = true;
            }
            else
            {
                clicked.IsUserChecked = false;
            }

            clicked.IsAutoPromoted = false;
        }
        finally
        {
            _suppressTopFrameSync = false;
        }

        if (becameChecked)
            Dispatcher.UIThread.Post(PromoteLargestAvailableTopFrame);
    }

    private void PromoteLargestAvailableTopFrame()
    {
        var preferred = _sizeToggles.FirstOrDefault(t => t is { Size: >= 256, IsUserChecked: true, IsAvailable: true });
        var active = preferred ?? _sizeToggles.Where(t => t is { Size: >= 256, IsAvailable: true }).MaxBy(t => t.Size);
        var autoPromoted = active != null && preferred == null;
        _suppressTopFrameSync = true;
        try
        {
            foreach (var t in _sizeToggles)
            {
                if (t.Size < 256) continue;
                var shouldCheck = t == active;
                t.IsChecked = shouldCheck;
                t.IsAutoPromoted = shouldCheck && autoPromoted;
            }
        }
        finally
        {
            _suppressTopFrameSync = false;
        }
    }

    private void EnforceTopFrameExclusivity(SizeToggle keep)
    {
        if (_suppressTopFrameSync)
            return;
        _suppressTopFrameSync = true;
        try
        {
            foreach (var t in _sizeToggles)
            {
                if (t == keep || t.Size < 256) continue;
                t.IsChecked = false;
                t.IsUserChecked = false;
                t.IsAutoPromoted = false;
            }

            keep.IsUserChecked = true;
        }
        finally
        {
            _suppressTopFrameSync = false;
        }
    }

    private void RebuildThumbs(bool fullRefill = false)
    {
        _rebuildChain = RebuildThumbsChainedAsync(_rebuildChain, fullRefill);
    }

    private void AttachSliderDragTracking(Slider slider)
    {
        slider.AddHandler(PointerPressedEvent, (_, _) => _sliderDragging = true, RoutingStrategies.Tunnel);
        slider.AddHandler(PointerReleasedEvent, (_, _) => OnSliderReleased(), RoutingStrategies.Tunnel);
        slider.AddHandler(PointerCaptureLostEvent, (_, _) => OnSliderReleased(), RoutingStrategies.Tunnel);
    }

    private bool AnyTransparentSource()
    {
        if (_sourceHasTransparency) return true;
        if (EffectiveMask > 0 || EffectivePadding > 0) return true;
        foreach (var t in _topThumbs.Concat(_smallThumbs))
        {
            if (t.OverrideHasTransparency) return true;
            if ((t.MaskRadiusOverride ?? 0) > 0) return true;
            if ((t.PaddingOverride ?? 0) > 0) return true;
        }

        return false;
    }

    private void UpdateBgColorAvailability()
    {
        if (_bgColorToggleCtrl == null)
            return;
        if (_debugPreview)
        {
            _bgColorToggleCtrl.IsEnabled = false;
            return;
        }

        var any = AnyTransparentSource();
        _bgColorToggleCtrl.IsEnabled = any;
        if (!any && _backgroundColor.HasValue)
            _backgroundColor = null;
    }

    private void ShowBackgroundColorFlyout()
    {
        if (_bgColorToggleCtrl == null)
            return;

        var view = new ColorView
        {
            Color = _lastBackgroundColor,
            IsAlphaEnabled = true,
            IsColorPaletteVisible = true,
            IsColorPreviewVisible = true,
            IsAccentColorsVisible = false,
            Width = 360,
            Height = 420
        };

        var viewScaled = new LayoutTransformControl
        {
            LayoutTransform = new ScaleTransform(0.8, 0.8),
            Child = view
        };

        var clearBtn = new Button
        {
            Content = "Clear",
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 10, 6),
            IsEnabled = _backgroundColor.HasValue
        };

        var panel = new StackPanel
        {
            Children = { viewScaled, clearBtn }
        };

        var flyout = new Flyout
        {
            Content = panel,
            Placement = PlacementMode.BottomEdgeAlignedRight,
            ShowMode = FlyoutShowMode.Transient
        };

        view.ColorChanged += (_, e) =>
        {
            if (e.OldColor == e.NewColor)
                return;
            var c = e.NewColor;
            _lastBackgroundColor = c;
            _backgroundColor = new SKColor(c.R, c.G, c.B, c.A);
            clearBtn.IsEnabled = true;
            DebouncedBgRebuild();
        };

        clearBtn.Click += (_, _) =>
        {
            _backgroundColor = null;
            clearBtn.IsEnabled = false;
            RebuildThumbs(true);
        };

        flyout.ShowAt(_bgColorToggleCtrl);
    }

    private SKBitmap? EffectiveSourceForControls()
    {
        return _focusedThumb != null ? SourceFor(_focusedThumb) : _sourceImage;
    }

    private void UpdateFitModeAvailability()
    {
        if (_fitModeButtonCtrl == null)
            return;
        var src = EffectiveSourceForControls();
        var square = _debugPreview || src != null && src.Width == src.Height;
        _fitModeButtonCtrl.IsEnabled = !square;
        if (!square)
            return;
        if (_focusedThumb != null)
        {
            if (_focusedThumb.FitModeOverride is { } m && m != FitMode.Fit)
            {
                _focusedThumb.FitModeOverride = FitMode.Fit;
                _focusedThumb.NotifyOverridesChanged();
            }
        }
        else
        {
            _fitMode = FitMode.Fit;
        }

        UpdateFitModeLabel();
    }

    private void UpdateFitModeLabel()
    {
        if (_fitModeButtonCtrl == null)
            return;
        var mode = _focusedThumb != null ? _focusedThumb.FitModeOverride ?? _fitMode : _fitMode;
        _fitIconCtrl?.IsVisible = mode == FitMode.Fit;
        _stretchIconCtrl?.IsVisible = mode == FitMode.Stretch;
        _cropIconCtrl?.IsVisible = mode == FitMode.Crop;
        ToolTip.SetTip(_fitModeButtonCtrl, mode switch
        {
            FitMode.Stretch => "Stretch to fill",
            FitMode.Crop => "Crop to fill",
            _ => "Fit inside"
        });
    }

    private void SyncControlsToFocus()
    {
        var mask = _focusedThumb != null ? _focusedThumb.MaskRadiusOverride ?? _maskRadius : _maskRadius;
        var pad = _focusedThumb != null ? _focusedThumb.PaddingOverride ?? _padding : _padding;
        _maskSliderCtrl?.Value = mask * 100;
        _maskValueCtrl?.Text = ((int)Math.Round(mask * 100)).ToString();
        _paddingSliderCtrl?.Value = pad * 100;
        _paddingValueCtrl?.Text = ((int)Math.Round(pad * 100)).ToString();
        UpdateFitModeLabel();

        var on = _focusedThumb != null;
        ApplyFocusClass(_maskSliderCtrl, on);
        ApplyFocusClass(_maskValueCtrl, on);
        ApplyFocusClass(_paddingSliderCtrl, on);
        ApplyFocusClass(_paddingValueCtrl, on);
        ApplyFocusClass(_fitModeButtonCtrl, on);
        ApplyFocusClass(_adjustButtonCtrl, on);
        UpdateFitModeAvailability();
    }

    // Padding slider is 0..10 with sub-integer precision; one decimal matches the step granularity.
    private static string FormatPadding(double sliderValue)
    {
        return Math.Round(sliderValue, 1).ToString("0.0", CultureInfo.InvariantCulture);
    }

    private static void ApplyFocusClass(StyledElement? c, bool on)
    {
        if (c == null)
            return;
        var has = c.Classes.Contains("focusMode");
        switch (on)
        {
            case true when !has:
                c.Classes.Add("focusMode");
                break;
            case false when has:
                c.Classes.Remove("focusMode");
                break;
        }
    }

    private void SetPaddingDragHighlight(bool on)
    {
        if (_focusedThumb != null)
        {
            foreach (var t in _topThumbs.Concat(_smallThumbs))
                t.IsPaddingDragging = on && t == _focusedThumb;
            return;
        }

        foreach (var t in _topThumbs)
            t.IsPaddingDragging = on;
        foreach (var t in _smallThumbs)
            t.IsPaddingDragging = false;
    }

    private void OnSliderReleased()
    {
        if (!_sliderDragging)
            return;
        _sliderDragging = false;
        foreach (var t in _topThumbs.Concat(_smallThumbs))
            t.DisposeContent();
        RebuildThumbs();
    }

    private void DebouncedRebuild()
    {
        _maskDebounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _maskDebounceCts = cts;
        _ = Task.Delay(120, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled)
                return;
            Dispatcher.UIThread.Post(() =>
            {
                if (!cts.IsCancellationRequested)
                    RebuildThumbs(true);
            });
        }, TaskScheduler.Default);
    }

    private void SyncThumbCollection(ObservableCollection<IconThumb> col, List<int> desired)
    {
        for (var i = col.Count - 1; i >= 0; i--)
        {
            if (desired.Contains(col[i].Size))
                continue;
            if (_focusedThumb == col[i])
                SetFocus(null);
            col[i].ClearOverrides();
            col[i].DisposeContent();
            col.RemoveAt(i);
        }

        for (var i = 0; i < desired.Count; i++)
        {
            if (i < col.Count && col[i].Size == desired[i])
                continue;
            col.Insert(i, new IconThumb(desired[i]));
        }
    }

    private async Task RebuildThumbsChainedAsync(Task previous, bool fullRefill)
    {
        if (_rebuildCts != null)
            await _rebuildCts.CancelAsync();
        try
        {
            await previous;
        }
        catch
        {
            // ignored — cancellation of prior run
        }

        var cts = new CancellationTokenSource();
        _rebuildCts = cts;
        var ct = cts.Token;

        var source = _sourceImage;
        if (source == null)
        {
            foreach (var t in _topThumbs.Concat(_smallThumbs))
                t.DisposeContent();
            _topThumbs.Clear();
            _smallThumbs.Clear();
            _smallPanel?.MaxHeight = double.PositiveInfinity;
            Opacity = 1;
            return;
        }

        if (_sliderDragging)
        {
            if (_debugPreview)
                return;
            var live = _focusedThumb ?? _topThumbs.FirstOrDefault();
            if (live == null)
                return;
            var liveSource = SourceFor(live);
            if (liveSource == null)
                return;
            var liveMask = MaskFor(live);
            var livePad = PaddingFor(live);
            var liveFit = FitModeFor(live);
            var liveCx = CropXFor(live);
            var liveCy = CropYFor(live);
            var liveZoom = ZoomFor(live);
            try
            {
                var (resized, preview) = await Task.Run(() =>
                {
                    var r = IconFactory.ResizePadded(liveSource, live.Size, liveMask, livePad, liveFit, liveCx, liveCy, liveZoom, _backgroundColor, _fxState.Shadow, _fxState.Stroke);
                    var p = IconThumb.ToAvaloniaBitmap(r);
                    return (r, p);
                }, ct);
                if (ct.IsCancellationRequested)
                {
                    resized.Dispose();
                    preview.Dispose();
                    return;
                }

                await Dispatcher.UIThread.InvokeAsync(() => live.Fill(resized, preview, false), DispatcherPriority.Background);
            }
            catch (OperationCanceledException)
            {
            }

            return;
        }

        if (fullRefill)
            foreach (var t in _topThumbs.Concat(_smallThumbs))
                t.DisposeContent();

        var maxDim = Math.Max(source.Width, source.Height);
        var desired = ActiveToggles
                      .Where(t => t is { IsChecked: true, IsAvailable: true })
                      .Select(t => t.Size)
                      .Where(s => s <= maxDim)
                      .OrderByDescending(s => s)
                      .ToList();

        SyncThumbCollection(_topThumbs, desired.Where(s => s >= 256).ToList());
        SyncThumbCollection(_smallThumbs, desired.Where(s => s < 256).ToList());
        UpdateBgColorAvailability();

        if (_smallPanel != null)
        {
            var topSize = _topThumbs.FirstOrDefault()?.Size;
            _smallPanel.MaxHeight = topSize != null
                ? ComputeSmallColumnCap(topSize.Value)
                : double.PositiveInfinity;
        }

        var toFill = _topThumbs.Concat(_smallThumbs).Where(t => t.Preview == null).ToList();

        if (_debugPreview)
        {
            var all = _topThumbs.Concat(_smallThumbs).ToList();
            for (var i = 0; i < all.Count; i++)
            {
                if (all[i].Preview != null)
                    continue;
                var (dbgSrc, dbgPreview) = RenderDebugFrame(all[i].Size, i, all.Count, EffectiveMask, EffectivePadding);
                all[i].Fill(dbgSrc, dbgPreview, true);
            }
        }
        else
        {
            var fillTasks = toFill.Select(thumb => Task.Run(async () =>
            {
                ct.ThrowIfCancellationRequested();
                var thumbSource = SourceFor(thumb) ?? source;
                var resized = IconFactory.ResizePadded(thumbSource, thumb.Size, MaskFor(thumb), PaddingFor(thumb), FitModeFor(thumb), CropXFor(thumb), CropYFor(thumb), ZoomFor(thumb), _backgroundColor, _fxState.Shadow, _fxState.Stroke);
                var preview = IconThumb.ToAvaloniaBitmap(resized);
                if (ct.IsCancellationRequested)
                {
                    resized.Dispose();
                    preview.Dispose();
                    return;
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (ct.IsCancellationRequested)
                    {
                        resized.Dispose();
                        preview.Dispose();
                        return;
                    }

                    thumb.Fill(resized, preview, false);
                }, DispatcherPriority.Background);
            }, ct)).ToArray();

            try
            {
                await Task.WhenAll(fillTasks);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        ApplyTitlebarIcon();
        Opacity = 1;
    }

    private void ApplyTitlebarIcon()
    {
        if (_debugPreview)
        {
            var frames = _topThumbs.Concat(_smallThumbs)
                                   .Where(t => t.Source != null)
                                   .OrderByDescending(t => t.Size)
                                   .Select(t => IconFactory.Copy(t.Source!))
                                   .ToList();
            if (frames.Count == 0)
                return;
            try
            {
                using var ms = new MemoryStream();
                IconFactory.Save(frames, ms);
                var bytes = ms.ToArray();
                Icon = new WindowIcon(new MemoryStream(bytes));
                ShowDebugTrayIcon(bytes);
                _iconIsDebug = true;
            }
            finally
            {
                foreach (var f in frames)
                    f.Dispose();
            }

            return;
        }

        HideDebugTrayIcon();
        if (!_iconIsDebug)
            return;
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://ImageToIcon/Assets/ProgramIcon.ico"));
            Icon = new WindowIcon(stream);
        }
        catch
        {
            // ignored
        }

        _iconIsDebug = false;
    }

    private void ShowDebugTrayIcon(byte[] icoBytes)
    {
        var app = Application.Current;
        if (app == null)
            return;

        var icon = new WindowIcon(new MemoryStream(icoBytes));
        if (_trayIcon == null)
        {
            _trayIcon = new TrayIcon
            {
                Icon = icon,
                ToolTipText = "ImageToIcon (debug preview)",
                IsVisible = true
            };
            TrayIcon.SetIcons(app, [_trayIcon]);
            return;
        }

        _trayIcon.Icon = icon;
        _trayIcon.IsVisible = true;
    }

    private void HideDebugTrayIcon()
    {
        _trayIcon?.IsVisible = false;
    }

    private static (SKBitmap src, Bitmap preview) RenderDebugFrame(int size, int index, int total, double maskRadius, double padding)
    {
        var bg = IntenseColorFor(index, total);
        var pixelSize = new PixelSize(size, size);
        var rtb = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
        var padPx = (int)(Math.Clamp(padding, 0, 1) * size);
        var innerRect = new Rect(padPx, padPx, Math.Max(1, size - 2 * padPx), Math.Max(1, size - 2 * padPx));
        using (var ctx = rtb.CreateDrawingContext())
        {
            DrawingContext.PushedState? clip = null;
            if (maskRadius > 0)
            {
                var r = Math.Clamp(maskRadius, 0, 1) * innerRect.Width / 2.0;
                clip = ctx.PushGeometryClip(new RectangleGeometry(innerRect, r, r));
            }

            ctx.FillRectangle(new SolidColorBrush(bg), innerRect);
            var label = size.ToString();
            var fontSize = Math.Max(8, size * 0.65);
            var text = new FormattedText(
                label,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(Typeface.Default.FontFamily, weight: FontWeight.Bold),
                fontSize,
                Brushes.White);
            var maxWidth = size * 0.85;
            if (text.Width > maxWidth)
            {
                fontSize *= maxWidth / text.Width;
                text = new FormattedText(
                    label,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(Typeface.Default.FontFamily, weight: FontWeight.Bold),
                    fontSize,
                    Brushes.White);
            }

            var pos = new Point(
                (size - text.Width) / 2,
                (size - text.Height) / 2);
            ctx.DrawText(text, pos);
            clip?.Dispose();
        }

        using var ms = new MemoryStream();
        rtb.Save(ms, new PngBitmapEncoderOptions());
        var bytes = ms.ToArray();
        var img = SKBitmap.Decode(bytes) ?? throw new InvalidOperationException("Failed to decode debug frame.");
        var bmp = new Bitmap(new MemoryStream(bytes));
        rtb.Dispose();
        return (img, bmp);
    }

    private static Color IntenseColorFor(int index, int total)
    {
        var step = total > 0 ? 360.0 / total : 137.508;
        var hue = index * step % 360;
        var sat = 0.75 + index % 3 * 0.08;
        var light = 0.45 + index % 2 * 0.1;
        return FromHsl(hue, sat, light);
    }

    private static Color FromHsl(double h, double s, double l)
    {
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = l - c / 2;
        double r, g, b;
        switch (h)
        {
            case < 60:
                r = c;
                g = x;
                b = 0;
                break;
            case < 120:
                r = x;
                g = c;
                b = 0;
                break;
            case < 180:
                r = 0;
                g = c;
                b = x;
                break;
            case < 240:
                r = 0;
                g = x;
                b = c;
                break;
            case < 300:
                r = x;
                g = 0;
                b = c;
                break;
            default:
                r = c;
                g = 0;
                b = x;
                break;
        }

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    // Start with the top-frame height as column cap; if columns would push the
    // window past the screen width, grow the cap so fewer columns are needed —
    // window trades horizontal for vertical growth. Scrollbars only kick in
    // once the cap already fills the screen height.
    private double ComputeSmallColumnCap(int topSize)
    {
        const double thumbLabelPadding = 20;
        const double thumbMargin = 6;
        const double horizontalChrome = 80;
        const double verticalChrome = 220;

        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen == null || _smallThumbs.Count == 0)
            return topSize + thumbLabelPadding;

        var scale = RenderScaling <= 0 ? 1.0 : RenderScaling;
        var screenW = screen.WorkingArea.Width / scale;
        var screenH = screen.WorkingArea.Height / scale;
        var topW = topSize + thumbMargin;
        var availSmallW = Math.Max(100, screenW - topW - horizontalChrome);
        var maxCap = Math.Max(topSize + thumbLabelPadding, screenH - verticalChrome);

        var cap = topSize + thumbLabelPadding;
        const double step = 32;
        while (true)
        {
            var totalW = MeasureColumnsWidth(cap, thumbLabelPadding, thumbMargin);
            if (totalW <= availSmallW || cap >= maxCap)
                return cap;
            cap = Math.Min(cap + step, maxCap);
        }
    }

    private double MeasureColumnsWidth(double cap, double labelPadding, double margin)
    {
        double totalW = 0, colH = 0, colW = 0;
        foreach (var t in _smallThumbs)
        {
            var h = t.Size + labelPadding;
            var w = t.Size + margin;
            if (colH > 0 && colH + h > cap)
            {
                totalW += colW;
                colH = 0;
                colW = 0;
            }

            colH += h;
            colW = Math.Max(colW, w);
        }

        if (colH > 0)
            totalW += colW;
        return totalW;
    }

    private double MaskFor(IconThumb t)
    {
        return t.MaskRadiusOverride ?? EffectiveMask;
    }

    private double PaddingFor(IconThumb t)
    {
        return t.PaddingOverride ?? EffectivePadding;
    }

    private FitMode FitModeFor(IconThumb t)
    {
        return t.FitModeOverride ?? _fitMode;
    }

    private double CropXFor(IconThumb t)
    {
        return t.CropOffsetXOverride ?? _cropOffsetX;
    }

    private double CropYFor(IconThumb t)
    {
        return t.CropOffsetYOverride ?? _cropOffsetY;
    }

    private double ZoomFor(IconThumb t)
    {
        return t.CropZoomOverride ?? _cropZoom;
    }

    private SKBitmap? SourceFor(IconThumb t)
    {
        return t.OverrideSource ?? _sourceImage;
    }

    private void SetFocus(IconThumb? thumb)
    {
        if (_focusedThumb == thumb)
            return;
        _focusedThumb?.IsFocused = false;
        _focusedThumb = thumb;
        _focusedThumb?.IsFocused = true;
        SyncControlsToFocus();
    }

    private void SnapshotGlobalsInto(IconThumb t)
    {
        t.MaskRadiusOverride ??= EffectiveMask;
        t.PaddingOverride ??= EffectivePadding;
        t.FitModeOverride ??= _fitMode;
        t.CropOffsetXOverride ??= _cropOffsetX;
        t.CropOffsetYOverride ??= _cropOffsetY;
        t.CropZoomOverride ??= _cropZoom;
        t.NotifyOverridesChanged();
    }

    private async Task OpenImageAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Image",
            AllowMultiple = false,
            FileTypeFilter = ImageLoader.BuildFilePickerFilters()
        });
        if (files.Count == 0)
            return;
        var file = files[0];
        LoadFromPath(file.Path.LocalPath);
    }

    private async Task SaveIconAsync()
    {
        if (_topThumbs.Count == 0 && _smallThumbs.Count == 0)
            return;
        // Wait for any in-flight rebuild so every thumb has its source ready.
        try
        {
            await _rebuildChain;
        }
        catch
        {
            // ignored
        }

        var baseName = _sourceName ?? "icon";
        var options = _format switch
        {
            IconFormat.Icns => new FilePickerSaveOptions
            {
                Title = "Save Icon",
                SuggestedFileName = baseName + ".icns",
                DefaultExtension = "icns",
                FileTypeChoices = [new FilePickerFileType("Apple icon") { Patterns = ["*.icns"] }]
            },
            IconFormat.Hicolor => new FilePickerSaveOptions
            {
                Title = "Save hicolor icon set (filename = base name)",
                SuggestedFileName = baseName + ".png",
                DefaultExtension = "png",
                FileTypeChoices =
                [
                    new FilePickerFileType("hicolor folder (PNG set)") { Patterns = ["*.png"] },
                    new FilePickerFileType("hicolor archive (tar.gz)") { Patterns = ["*.tar.gz"] }
                ]
            },
            _ => new FilePickerSaveOptions
            {
                Title = "Save Icon",
                SuggestedFileName = baseName + ".ico",
                DefaultExtension = "ico",
                FileTypeChoices = [new FilePickerFileType("Windows icon") { Patterns = ["*.ico"] }]
            }
        };
        var file = await StorageProvider.SaveFilePickerAsync(options);
        if (file == null)
            return;
        var ordered = _topThumbs.Concat(_smallThumbs)
                                .OrderByDescending(t => t.Size)
                                .ToList();
        var images = new List<SKBitmap>(ordered.Count);
        try
        {
            foreach (var t in ordered)
            {
                if (t.Source != null)
                {
                    images.Add(IconFactory.Copy(t.Source));
                }
                else
                {
                    var src = SourceFor(t);
                    if (src != null)
                        images.Add(IconFactory.ResizePadded(src, t.Size, MaskFor(t), PaddingFor(t), FitModeFor(t), CropXFor(t), CropYFor(t), ZoomFor(t), _backgroundColor, _fxState.Shadow, _fxState.Stroke));
                }
            }

            var path = file.Path.LocalPath;
            switch (_format)
            {
                case IconFormat.Icns:
                    IconFactory.SaveIcns(images, path);
                    break;
                case IconFormat.Hicolor:
                    var name = Path.GetFileName(path);
                    if (name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
                    {
                        var archiveStem = name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
                            ? name[..^7]
                            : name[..^4];
                        if (string.IsNullOrEmpty(archiveStem))
                            archiveStem = baseName;
                        IconFactory.SaveHicolorArchive(images, path, archiveStem);
                    }
                    else
                    {
                        var dir = Path.GetDirectoryName(path) ?? ".";
                        var stem = Path.GetFileNameWithoutExtension(path);
                        if (string.IsNullOrEmpty(stem))
                            stem = baseName;
                        if (File.Exists(path) && new FileInfo(path).Length == 0)
                        {
                            try
                            {
                                File.Delete(path);
                            }
                            catch
                            {
                                /* best-effort */
                            }
                        }

                        IconFactory.SaveHicolorFolder(images, dir, stem);
                    }

                    break;
                default:
                    IconFactory.Save(images, path);
                    break;
            }
        }
        catch (Exception ex)
        {
            var dlg = new Window
            {
                Title = "Error",
                Width = 400, Height = 120,
                Content = new TextBlock { Text = ex.Message, Margin = new Thickness(12) }
            };
            await dlg.ShowDialog(this);
        }
        finally
        {
            foreach (var img in images)
                img.Dispose();
        }
    }

    private void OnWindowDragOver(object? sender, DragEventArgs e)
    {
        if (_debugPreview)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnWindowDrop(object? sender, DragEventArgs e)
    {
        if (_debugPreview)
            return;
        var files = e.DataTransfer.TryGetFiles();
        var path = files?.FirstOrDefault()?.Path.LocalPath;
        if (path == null || !ImageLoader.IsSupported(path))
            return;

        // If dropped on a specific thumb, replace only that one
        if (e.Source is Control c && FindThumb(c) is { } thumb)
        {
            var newImg = ImageLoader.TryLoad(path);
            if (newImg == null)
                return;
            SnapshotGlobalsInto(thumb);
            thumb.SetOverrideSource(newImg);
            thumb.DisposeContent();
            UpdateFitModeAvailability();
            UpdateBgColorAvailability();
            RebuildThumbs();
            return;
        }

        LoadFromPath(path);
    }

    private static IconThumb? FindThumb(Control? c)
    {
        while (c != null)
        {
            if (c.Tag is IconThumb t)
                return t;
            c = c.Parent as Control;
        }

        return null;
    }

    private void InsertSorted(SizeToggle t)
    {
        var i = 0;
        while (i < _sizeToggles.Count && _sizeToggles[i].Size > t.Size)
            i++;
        _sizeToggles.Insert(i, t);
    }

    private async Task AddCustomSizeAsync()
    {
        var defaults = new HashSet<int>(IconFactory.DefaultSizes);
        var currentCustom = _sizeToggles.Where(t => t.IsCustom).Select(t => t.Size).ToHashSet();
        var result = await AddSizesDialog.ShowAsync(this, defaults, currentCustom);
        if (result == null)
            return;

        var target = new HashSet<int>(result);
        foreach (var t in _sizeToggles.Where(t => t.IsCustom && !target.Contains(t.Size)).ToList())
        {
            t.PropertyChanged -= OnTogglePropertyChanged;
            _sizeToggles.Remove(t);
        }

        var existing = _sizeToggles.Select(t => t.Size).ToHashSet();
        foreach (var toggle in from size in target where !existing.Contains(size) select new SizeToggle(size, true, true))
        {
            toggle.PropertyChanged += OnTogglePropertyChanged;
            InsertSorted(toggle);
        }

        UpdateAvailability();
        var topKeep = _sizeToggles
                      .Where(t => t is { Size: >= 256, IsChecked: true })
                      .MaxBy(t => t.Size);
        if (topKeep != null)
            EnforceTopFrameExclusivity(topKeep);
        RebuildThumbs();
    }

    private void OnThumbPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_debugPreview)
            return;
        if (sender is not Control { Tag: IconThumb thumb } c)
            return;
        var props = e.GetCurrentPoint(c).Properties;
        var right = props.IsRightButtonPressed;
        if (!props.IsLeftButtonPressed && !right)
            return;

        _pressedThumb = thumb;
        _pressedThumbWasRight = right;
        _pressPoint = e.GetPosition(this);
        _panStartCropX = CropXFor(thumb);
        _panStartCropY = CropYFor(thumb);
        _panActive = false;
        _pressMovedBeyondThreshold = false;
        e.Pointer.Capture(c);
    }

    private void OnThumbPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressedThumb == null)
            return;
        var panSource = SourceFor(_pressedThumb);
        if (panSource == null)
            return;

        var pos = e.GetPosition(this);
        var dx = pos.X - _pressPoint.X;
        var dy = pos.Y - _pressPoint.Y;

        if (!_pressMovedBeyondThreshold)
        {
            if (Math.Abs(dx) < PanThresholdPx && Math.Abs(dy) < PanThresholdPx)
                return;
            _pressMovedBeyondThreshold = true;
            if (FitModeFor(_pressedThumb) != FitMode.Crop)
                return;
            _panActive = true;
            _sliderDragging = true;
        }

        if (!_panActive)
            return;

        var thumb = _pressedThumb;
        var pad = PaddingFor(thumb);
        var padPx = (int)(Math.Clamp(pad, 0, 1) * thumb.Size);
        var inner = Math.Max(1, thumb.Size - 2 * padPx);
        var zoom = Math.Max(1.0, ZoomFor(thumb));
        var cover = Math.Max((double)inner / panSource.Width, (double)inner / panSource.Height) * zoom;
        var w = panSource.Width * cover;
        var h = panSource.Height * cover;
        var overflowX = Math.Max(1e-6, w - inner);
        var overflowY = Math.Max(1e-6, h - inner);

        var newX = Math.Clamp(_panStartCropX + 2.0 * dx / overflowX, -1.0, 1.0);
        var newY = Math.Clamp(_panStartCropY + 2.0 * dy / overflowY, -1.0, 1.0);

        if (_focusedThumb == thumb)
        {
            thumb.CropOffsetXOverride = newX;
            thumb.CropOffsetYOverride = newY;
            thumb.NotifyOverridesChanged();
        }
        else if (thumb.IsDecoupled)
        {
            thumb.CropOffsetXOverride = newX;
            thumb.CropOffsetYOverride = newY;
        }
        else
        {
            _cropOffsetX = newX;
            _cropOffsetY = newY;
        }

        RebuildThumbs();
    }

    private void OnThumbPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        if (sender is not Control { Tag: IconThumb thumb })
            return;
        if (FitModeFor(thumb) != FitMode.Crop)
            return;
        var step = e.Delta.Y >= 0 ? 1.1 : 1 / 1.1;
        var current = ZoomFor(thumb);
        var next = Math.Clamp(current * step, 1.0, 10.0);
        if (Math.Abs(next - current) < 1e-4)
            return;

        if (_focusedThumb == thumb)
        {
            thumb.CropZoomOverride = next;
            thumb.NotifyOverridesChanged();
        }
        else if (thumb.IsDecoupled)
        {
            thumb.CropZoomOverride = next;
        }
        else
        {
            _cropZoom = next;
        }

        if (next <= 1.0)
        {
            if (_focusedThumb == thumb || thumb.IsDecoupled)
            {
                thumb.CropOffsetXOverride = 0;
                thumb.CropOffsetYOverride = 0;
            }
            else
            {
                _cropOffsetX = 0;
                _cropOffsetY = 0;
            }
        }

        e.Handled = true;
        _sliderDragging = true;
        RebuildThumbs();
        DebouncedZoomRebuild();
    }

    private void DebouncedBgRebuild()
    {
        _bgDebounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _bgDebounceCts = cts;
        _ = Task.Delay(40, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled)
                return;
            Dispatcher.UIThread.Post(() =>
            {
                if (cts.IsCancellationRequested)
                    return;
                RebuildThumbs(true);
            });
        }, TaskScheduler.Default);
    }

    private void DebouncedZoomRebuild()
    {
        _zoomDebounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _zoomDebounceCts = cts;
        _ = Task.Delay(150, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled)
                return;
            Dispatcher.UIThread.Post(() =>
            {
                if (cts.IsCancellationRequested)
                    return;
                OnSliderReleased();
            });
        }, TaskScheduler.Default);
    }

    private async void OnThumbPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        try
        {
            if (_pressedThumb == null)
                return;
            var thumb = _pressedThumb;
            var wasPan = _panActive;
            var wasRight = _pressedThumbWasRight;
            _pressedThumb = null;
            _panActive = false;
            e.Pointer.Capture(null);

            if (wasPan)
            {
                OnSliderReleased();
                return;
            }

            if (wasRight)
            {
                SetFocus(_focusedThumb == thumb ? null : thumb);
                return;
            }

            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = false,
                FileTypeFilter = ImageLoader.BuildFilePickerFilters()
            });
            if (files.Count == 0)
                return;
            var file = files[0];
            var img = ImageLoader.TryLoad(file.Path.LocalPath);
            if (img == null)
                return;
            SnapshotGlobalsInto(thumb);
            thumb.SetOverrideSource(img);
            thumb.DisposeContent();
            UpdateFitModeAvailability();
            UpdateBgColorAvailability();
            RebuildThumbs();
        }
        catch
        {
            // ignored
        }
    }

    private void OnDecoupleIconClicked(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Control { DataContext: IconThumb thumb })
            return;
        if (_focusedThumb == thumb)
            SetFocus(null);
        thumb.ClearOverrides();
        UpdateFitModeAvailability();
        UpdateBgColorAvailability();
        RebuildThumbs(true);
    }

    private void OnThumbPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_pressedThumb == null)
            return;
        _pressedThumb = null;
        if (!_panActive)
            return;
        _panActive = false;
        OnSliderReleased();
    }

    private void RefreshUpdateButton()
    {
        var version = _selfUpdate.Pending?.Version
                      ?? (Version.TryParse(_settings.PendingUpdateVersion, out var v) ? v : null);
        if (version == null)
        {
            _updateBtn.IsVisible = false;
            return;
        }

        _updateBtn.Content = $"Update {version}";
        ToolTip.SetTip(_updateBtn, $"Install ImageToIcon {version}");
        _updateBtn.IsVisible = true;
    }

    private async Task CheckForUpdateAsync()
    {
        try
        {
            await _selfUpdate.CheckAsync();
        }
        catch
        {
            // best-effort
        }

        RefreshUpdateButton();
    }

    private async Task OnUpdateClickAsync()
    {
        var pending = _selfUpdate.Pending;
        var version = pending?.Version
                      ?? (Version.TryParse(_settings.PendingUpdateVersion, out var v) ? v : null);
        if (version == null)
            return;

        var body = await SelfUpdater.FetchLatestBodyAsync(CancellationToken.None);
        var confirmed = await ChangelogDialog.ShowAsync(this, version, body);
        if (!confirmed)
            return;

        _updateBtn.IsEnabled = false;
        try
        {
            await UpdateProgressDialog.RunAsync(this, _selfUpdate);
        }
        finally
        {
            _updateBtn.IsEnabled = true;
        }
    }
}