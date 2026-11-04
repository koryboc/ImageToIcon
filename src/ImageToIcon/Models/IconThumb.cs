using System.ComponentModel;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace ImageToIcon.Models;

public class IconThumb(int size) : INotifyPropertyChanged
{
    public int Size { get; } = size;

    // Kept only for non-regeneratable pixels (debug, drag-replace); normal rebuilds re-render on save.
    public SKBitmap? Source { get; private set; }
    public Bitmap? Preview { get; private set; }

    public string SizeLabel => Size.ToString();
    public string TooltipText => $"{Size}x{Size}";
    public int DisplaySize => Size;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Fill(SKBitmap newSource, Bitmap newPreview, bool keepSource)
    {
        Source?.Dispose();
        Preview?.Dispose();
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
    }

    public void Replace(SKBitmap newSource)
    {
        var preview = ToAvaloniaBitmap(newSource);
        Fill(newSource, preview, true);
    }

    public void DisposeContent()
    {
        Source?.Dispose();
        Preview?.Dispose();
        Source = null;
        Preview = null;
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

        using (var fb = wb.Lock())
        {
            var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var pm = img.PeekPixels();
            if (pm == null || !pm.ReadPixels(info, fb.Address, fb.RowBytes))
                throw new InvalidOperationException("Failed to read SKBitmap pixels into WriteableBitmap.");
        }

        return wb;
    }

    private void Raise(string name)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}