using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
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

public partial class MainWindow : Window
{
    private readonly CancellationTokenSource _cts = new();
    private readonly SelfUpdateCoordinator _selfUpdate;
    private readonly Settings _settings;
    private readonly ObservableCollection<SizeToggle> _sizeToggles = [];
    private readonly ItemsControl? _smallPanel;
    private readonly ObservableCollection<IconThumb> _smallThumbs = [];
    private readonly ObservableCollection<IconThumb> _topThumbs = [];
    private readonly Button _updateBtn;
    private bool _debugPreview;
    private bool _iconIsDebug;
    private CancellationTokenSource? _maskDebounceCts;
    private double _maskRadius;
    private double _padding;
    private Task _rebuildChain = Task.CompletedTask;
    private CancellationTokenSource? _rebuildCts;
    private SKBitmap? _sourceImage;
    private string? _sourceName;

    private bool _suppressTopFrameSync;
    private TrayIcon? _trayIcon;

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

        var openBtn = this.FindControl<Button>("OpenBtn")!;
        var saveBtn = this.FindControl<Button>("SaveBtn")!;
        var addSizeBtn = this.FindControl<Button>("AddSizeBtn")!;
        _updateBtn = this.FindControl<Button>("UpdateBtn")!;
        var topPanel = this.FindControl<ItemsControl>("TopThumbs")!;
        _smallPanel = this.FindControl<ItemsControl>("SmallThumbs")!;
        var toggles = this.FindControl<ItemsControl>("SizeToggles")!;
        var borderToggle = this.FindControl<ToggleButton>("BorderToggle")!;
        borderToggle.IsChecked = _settings.ShowThumbBorders;
        var debugToggle = this.FindControl<ToggleButton>("DebugToggle")!;
        debugToggle.Click += (_, _) =>
        {
            _debugPreview = debugToggle.IsChecked == true;
            openBtn.IsEnabled = !_debugPreview;
            RebuildThumbs(true);
        };

        _maskRadius = Math.Clamp(_settings.MaskRadius, 0, 1);
        var maskSlider = this.FindControl<Slider>("MaskSlider")!;
        var maskValue = this.FindControl<TextBlock>("MaskValue")!;
        maskSlider.Value = _maskRadius * 100;
        maskValue.Text = ((int)Math.Round(_maskRadius * 100)).ToString();
        maskSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty)
                return;
            var v = maskSlider.Value / 100.0;
            maskValue.Text = ((int)Math.Round(maskSlider.Value)).ToString();
            if (Math.Abs(v - _maskRadius) < 0.0001)
                return;
            _maskRadius = v;
            DebouncedRebuild();
        };

        _padding = Math.Clamp(_settings.Padding, 0, 0.1);
        var paddingSlider = this.FindControl<Slider>("PaddingSlider")!;
        var paddingValue = this.FindControl<TextBlock>("PaddingValue")!;
        paddingSlider.Value = _padding * 100;
        paddingValue.Text = ((int)Math.Round(_padding * 100)).ToString();
        paddingSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty)
                return;
            var v = paddingSlider.Value / 100.0;
            paddingValue.Text = ((int)Math.Round(paddingSlider.Value)).ToString();
            if (Math.Abs(v - _padding) < 0.0001)
                return;
            _padding = v;
            DebouncedRebuild();
        };

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
            _settings.SelectedSizes = _sizeToggles.Where(t => t.IsChecked).Select(t => t.Size).ToArray();
            _settings.CustomSizes = _sizeToggles.Where(t => t.IsCustom).Select(t => t.Size).ToArray();
            _settings.ShowThumbBorders = borderToggle.IsChecked == true;
            _settings.MaskRadius = _maskRadius;
            _settings.Padding = _padding;
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
    }

    private void OnTogglePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SizeToggle.IsChecked))
            return;

        // Windows only ever picks the largest available frame from anything
        // >= 256, so treat that band as radio-style: checking one clears
        // the others, but nothing gets permanently disabled.
        if (sender is SizeToggle { Size: >= 256, IsChecked: true } changed)
            EnforceTopFrameExclusivity(changed);

        RebuildThumbs();
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
                if (t != keep && t.Size >= 256)
                    t.IsChecked = false;
            }
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

    private static void SyncThumbCollection(ObservableCollection<IconThumb> col, List<int> desired)
    {
        for (var i = col.Count - 1; i >= 0; i--)
        {
            if (desired.Contains(col[i].Size))
                continue;
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

        if (fullRefill)
            foreach (var t in _topThumbs.Concat(_smallThumbs))
                t.DisposeContent();

        var maxDim = Math.Max(source.Width, source.Height);
        var desired = _sizeToggles
                      .Where(t => t is { IsChecked: true, IsAvailable: true })
                      .Select(t => t.Size)
                      .Where(s => s <= maxDim)
                      .OrderByDescending(s => s)
                      .ToList();

        SyncThumbCollection(_topThumbs, desired.Where(s => s >= 256).ToList());
        SyncThumbCollection(_smallThumbs, desired.Where(s => s < 256).ToList());

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
                var (dbgSrc, dbgPreview) = RenderDebugFrame(all[i].Size, i, all.Count, _maskRadius, _padding);
                all[i].Fill(dbgSrc, dbgPreview, true);
            }
        }
        else
        {
            var fillTasks = toFill.Select(thumb => Task.Run(async () =>
            {
                ct.ThrowIfCancellationRequested();
                var resized = IconFactory.ResizePadded(source, thumb.Size, _maskRadius, _padding);
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
            IDisposable? clip = null;
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

    private ObservableCollection<IconThumb> CollectionFor(int size)
    {
        return size >= 256 ? _topThumbs : _smallThumbs;
    }

    private async Task OpenImageAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open image",
            AllowMultiple = false,
            FileTypeFilter = ImageLoader.BuildFilePickerFilters()
        });
        var file = files.FirstOrDefault();
        if (file == null)
            return;
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

        var suggested = (_sourceName ?? "icon") + ".ico";
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save icon",
            SuggestedFileName = suggested,
            DefaultExtension = "ico",
            FileTypeChoices =
            [
                new FilePickerFileType("Icon file") { Patterns = ["*.ico"] }
            ]
        });
        if (file == null)
            return;
        var images = _topThumbs.Concat(_smallThumbs)
                               .Where(t => t.Source != null)
                               .OrderByDescending(t => t.Size)
                               .Select(t => IconFactory.Copy(t.Source!));
        try
        {
            IconFactory.Save(images, file.Path.LocalPath);
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
            var resized = IconFactory.ResizePadded(newImg, thumb.Size, _maskRadius, _padding);
            newImg.Dispose();
            var col = CollectionFor(thumb.Size);
            var idx = col.IndexOf(thumb);
            thumb.Replace(resized);
            col.RemoveAt(idx);
            col.Insert(idx, thumb);
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
        foreach (var size in target)
        {
            if (existing.Contains(size))
                continue;
            var toggle = new SizeToggle(size, true, true);
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

    private async void OnThumbPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        try
        {
            if (_debugPreview)
                return;
            if (sender is not Control { Tag: IconThumb thumb } c)
                return;
            if (!e.GetCurrentPoint(c).Properties.IsLeftButtonPressed)
                return;

            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = false,
                FileTypeFilter = ImageLoader.BuildFilePickerFilters()
            });
            var file = files.FirstOrDefault();
            if (file == null)
                return;
            var img = ImageLoader.TryLoad(file.Path.LocalPath);
            if (img == null)
                return;
            var resized = IconFactory.ResizePadded(img, thumb.Size, _maskRadius, _padding);
            img.Dispose();
            var col = CollectionFor(thumb.Size);
            var idx = col.IndexOf(thumb);
            thumb.Replace(resized);
            col.RemoveAt(idx);
            col.Insert(idx, thumb);
        }
        catch
        {
            // ignored
        }
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
