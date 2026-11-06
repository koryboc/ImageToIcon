using System.ComponentModel;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ImageToIcon.Services;
using SkiaSharp;

namespace ImageToIcon.Models;

public class IconThumb(int size) : INotifyPropertyChanged
{
    public int Size { get; } = size;

    public SKBitmap? Source { get; private set; }
    public Bitmap? Preview { get; private set; }

    public SKBitmap? OverrideSource { get; private set; }
    public bool OverrideHasTransparency { get; private set; }
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
        get;
        set
        {
            if (field == value)
                return;
            field = value;
            Raise(nameof(IsFocused));
        }
    }

    public bool IsPaddingDragging
    {
        get;
        set
        {
            if (field == value)
                return;
            field = value;
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
        Preview = newPreview;
        if (keepSource)
        {
            Source = newSource;
        }
        else
        {
            newSource.Dispose();
            Source = null;
        }

        Raise(nameof(Source));
        Raise(nameof(Preview));
        oldSource?.Dispose();
    }

    public void Replace(SKBitmap newSource)
    {
        var preview = ToAvaloniaBitmap(newSource);
        Fill(newSource, preview, true);
    }

    public void DisposeContent()
    {
        Source?.Dispose();
        Source = null;
        Preview = null;
    }

    public void SetOverrideSource(SKBitmap? newSource)
    {
        var old = OverrideSource;
        OverrideSource = newSource;
        OverrideHasTransparency = IconFactory.HasTransparency(newSource);
        Raise(nameof(OverrideSource));
        Raise(nameof(IsDecoupled));
        old?.Dispose();
    }

    public void NotifyOverridesChanged()
    {
        Raise(nameof(IsDecoupled));
    }

    public void ClearOverrides()
    {
        var old = OverrideSource;
        OverrideSource = null;
        OverrideHasTransparency = false;
        MaskRadiusOverride = null;
        PaddingOverride = null;
        FitModeOverride = null;
        CropOffsetXOverride = null;
        CropOffsetYOverride = null;
        Raise(nameof(OverrideSource));
        Raise(nameof(IsDecoupled));
        old?.Dispose();
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
