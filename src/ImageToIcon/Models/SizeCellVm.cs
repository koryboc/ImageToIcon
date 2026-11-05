using System.ComponentModel;

namespace ImageToIcon.Models;

public class SizeCellVm(int size, bool reserved, string tooltip) : INotifyPropertyChanged
{
    public int Size { get; } = size;
    public string SizeText => Size.ToString();
    public string Tooltip { get; } = tooltip;
    public bool IsReserved { get; } = reserved;

    public bool IsChecked
    {
        get;
        set
        {
            if (field == value)
                return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
        }
    } = reserved;

    public event PropertyChangedEventHandler? PropertyChanged;
}
