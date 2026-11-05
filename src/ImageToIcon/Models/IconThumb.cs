using System.ComponentModel;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ImageToIcon.Services;
using SkiaSharp;

namespace ImageToIcon.Models;

public class IconThumb(int size) : INotifyPropertyChanged
{
    private bool _isFocused;
    private bool _isPaddingDragging;
    public int Size { get; } = size;

    public SKBitmap? Source { get; private set; }
    public Bitmap? Preview { get; private set; }

    public SKBitmap? OverrideSource { get; private set; }
    public double? MaskRadiusOverride { get; set; }
    public double? PaddingOverride { get; set; }
    public FitMode? FitModeOverride { get; set; }
    public double? CropOffsetXOverride { get; set; }
    public double? CropOffsetYOverride { get; set; }

    public bool IsDecoupled => OverrideSource != null
                               || MaskRadiusOverride.HasValue
                               || PaddingOverride.HasValue
                               || FitModeOverride.HasValue
                               || CropOffsetXOverride.HasValue
                               || CropOffsetYOverride.HasValue;

    public bool IsFocused
    {
        get => _isFocused;
        set
        {
            if (_isFocused == value)
                return;
            _isFocused = value;
            Raise(nameof(IsFocused));
        }
    }

    public bool IsPaddingDragging
    {
        get => _isPaddingDragging;
        set
        {
            if (_isPaddingDragging == value)
                return;
            _isPaddingDragging = value;
            Raise(nameof(IsPaddingDragging));
        }
    }

    public string SizeLabel => Size.ToString();
    public string TooltipText => $"{Size}x{Size}";
    public int DisplaySize => Size;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Fill(SKBitmap newSource, Bitmap newPreview, bool keepSource)
    {
        var oldSource = Source;
        var oldPreview = Preview;
        Preview = newPreview;
        if (keepSource)
        {
            Source = newSource;
        }
        else
        {
            DeferDispose(newSource);
            Source = null;
        }

        Raise(nameof(Source));
        Raise(nameof(Preview));
        DeferDispose(oldSource);
        DeferDispose(oldPreview);
    }

    public void Replace(SKBitmap newSource)
    {
        var preview = ToAvaloniaBitmap(newSource);
        Fill(newSource, preview, true);
    }

    public void DisposeContent()
    {
        DeferDispose(Source);
        DeferDispose(Preview);
        Source = null;
        Preview = null;
    }

    public void SetOverrideSource(SKBitmap? newSource)
    {
        var old = OverrideSource;
        OverrideSource = newSource;
        Raise(nameof(OverrideSource));
        Raise(nameof(IsDecoupled));
        DeferDispose(old);
    }

    public void NotifyOverridesChanged()
    {
        Raise(nameof(IsDecoupled));
    }

    public void ClearOverrides()
    {
        var old = OverrideSource;
        OverrideSource = null;
        MaskRadiusOverride = null;
        PaddingOverride = null;
        FitModeOverride = null;
        CropOffsetXOverride = null;
        CropOffsetYOverride = null;
        Raise(nameof(OverrideSource));
        Raise(nameof(IsDecoupled));
        DeferDispose(old);
    }

    // Defer so the render thread can release its native ref before dispose.
    private static void DeferDispose(IDisposable? disposable)
    {
        if (disposable == null)
            return;
        Dispatcher.UIThread.Post(disposable.Dispose, DispatcherPriority.Background);
    }

    public static Bitmap ToAvaloniaBitmap(SKBitmap img)
    {
        var w = img.Width;
        var h = img.Height;
        var wb = new WriteableBitmap(
            new PixelSize(w, h),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        using var fb = wb.Lock();
        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var pm = img.PeekPixels();
        if (pm == null || !pm.ReadPixels(info, fb.Address, fb.RowBytes))
            throw new InvalidOperationException("Failed to read SKBitmap pixels into WriteableBitmap.");

        return wb;
    }

    private void Raise(string name)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
