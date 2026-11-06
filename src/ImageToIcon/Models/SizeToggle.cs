using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ImageToIcon.Models;

public class SizeToggle(int size, bool isChecked, bool isCustom = false) : INotifyPropertyChanged
{
    public int Size { get; } = size;
    public bool IsCustom { get; } = isCustom;
    public string Label => Size.ToString();

    public bool IsChecked
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            Raise();
        }
    } = isChecked;

    public bool IsAvailable
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            Raise();
        }
    } = true;

    public bool IsUserChecked
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            Raise();
        }
    } = isChecked;

    public bool IsAutoPromoted
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            Raise();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}