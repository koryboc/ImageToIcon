using System.ComponentModel;

namespace ImageToIcon.Models;

public class SizeCellVm(int size, bool reserved, string tooltip) : INotifyPropertyChanged
{
    private bool _isChecked = reserved;

    public int Size { get; } = size;
    public string SizeText => Size.ToString();
    public string Tooltip { get; } = tooltip;
    public bool IsReserved { get; } = reserved;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value)
                return;
            _isChecked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}