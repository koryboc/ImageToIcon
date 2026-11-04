using System.ComponentModel;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace ImageToIcon.Models;

public class IconThumb(int size) : INotifyPropertyChanged
{
    public int Size { get; } = size;
    public SKBitmap? Source { get; private set; }
    public Bitmap? Preview { get; private set; }

    public string SizeLabel => Size.ToString();
    public string TooltipText => $"{Size}x{Size}";
    public int DisplaySize => Size;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Fill(SKBitmap newSource, Bitmap newPreview)
    {
        Source?.Dispose();
        Preview?.Dispose();
        Source = newSource;
        Preview = newPreview;
        Raise(nameof(Source));
        Raise(nameof(Preview));
    }

    public void Replace(SKBitmap newSource)
    {
        Fill(newSource, ToAvaloniaBitmap(newSource));
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
        using var image = SKImage.FromBitmap(img);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var ms = new MemoryStream();
        data.SaveTo(ms);
        ms.Position = 0;
        return new Bitmap(ms);
    }

    private void Raise(string name)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}