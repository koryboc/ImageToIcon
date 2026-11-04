using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ImageToIcon.Models;
using ImageToIcon.Platform;
using ImageToIcon.Services;

namespace ImageToIcon.Views;

public class AddSizesDialog : Window
{
    private static readonly int[] Bases = [16, 24, 32, 48];

    private static readonly int[] Scales =
    [
        125, 150, 175, 200, 225, 250, 275, 300,
        325, 350, 375, 400, 425, 450, 475, 500
    ];

    // Windows never scales the 256 frame, it always picks the largest
    // available. These are the sensible replacements for the top slot.
    private static readonly int[] HighRes = [384, 512, 768, 1024];
    private readonly Button _addCustomBtn;
    private readonly Button _cancelBtn;

    private readonly Dictionary<int, SizeCellVm> _cellVms = new();
    private readonly TextBlock _customError;
    private readonly NumericUpDown _customInput;
    private readonly WrapPanel _customRow;

    private readonly List<SizeCellVm> _dpiCellVms = [];
    private readonly Grid _dpiGrid;
    private readonly ItemsControl _highResItems;
    private readonly Button _okBtn;
    private readonly Button _selectAllBtn;
    private HashSet<int> _reserved = [];
    private int[]? _result;

    public AddSizesDialog()
    {
        InitializeComponent();
        _dpiGrid = this.FindControl<Grid>("DpiGrid")!;
        _highResItems = this.FindControl<ItemsControl>("HighResItems")!;
        _customRow = this.FindControl<WrapPanel>("CustomRow")!;
        _customInput = this.FindControl<NumericUpDown>("CustomInput")!;
        _addCustomBtn = this.FindControl<Button>("AddCustomBtn")!;
        _customError = this.FindControl<TextBlock>("CustomError")!;
        _okBtn = this.FindControl<Button>("OkBtn")!;
        _cancelBtn = this.FindControl<Button>("CancelBtn")!;
        _selectAllBtn = this.FindControl<Button>("SelectAllBtn")!;
    }

    private static FontFamily MonoFont =>
        Application.Current!.FindResource("AppMonoFontFamily") as FontFamily
        ?? FontFamily.Default;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public static async Task<int[]?> ShowAsync(Window owner, HashSet<int> reserved, HashSet<int>? preselected = null)
    {
        var dlg = new AddSizesDialog();
        return await dlg.RunAsync(owner, reserved, preselected);
    }

    private async Task<int[]?> RunAsync(Window owner, HashSet<int> reserved, HashSet<int>? preselected)
    {
        _reserved = reserved;

        _customInput.Minimum = IconFactory.MinSize;
        _customInput.Maximum = IconFactory.MaxSize;

        BuildDpiGrid();
        PopulateHighRes();

        if (preselected != null)
        {
            foreach (var v in preselected.Where(v => !_reserved.Contains(v)))
            {
                if (_cellVms.TryGetValue(v, out var vm))
                    vm.IsChecked = true;
                else
                    AddCustomPill(v);
            }
        }

        _addCustomBtn.Click += (_, _) => OnAddCustom();
        _customInput.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;
            e.Handled = true;
            OnAddCustom();
        };
        _selectAllBtn.Click += (_, _) => OnSelectAllToggle();
        foreach (var vm in _dpiCellVms.Where(v => !v.IsReserved))
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SizeCellVm.IsChecked))
                    UpdateSelectAllLabel();
            };
        UpdateSelectAllLabel();

        _okBtn.Click += (_, _) =>
        {
            _result = CollectResult();
            Close();
        };
        _cancelBtn.Click += (_, _) => Close();

        Opened += (_, _) => Win32Window.ApplyDarkTitlebar(this);
        await ShowDialog(owner);
        return _result;
    }

    private SizeCellVm GetOrCreateVm(int size)
    {
        if (_cellVms.TryGetValue(size, out var existing))
            return existing;

        var vm = new SizeCellVm(size, _reserved.Contains(size), BuildTooltip(size));
        _cellVms[size] = vm;
        return vm;
    }

    private static string BuildTooltip(int size)
    {
        var lines = new List<string> { $"{size}\u00d7{size}" };
        lines.AddRange(from b in Bases from s in Scales where b * s / 100 == size select $"base {b} at {s}%");
        if (Array.IndexOf(HighRes, size) >= 0)
            lines.Add("replaces 256 as top frame");
        return string.Join('\n', lines);
    }

    private void BuildDpiGrid()
    {
        _dpiGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        foreach (var _ in Scales)
            _dpiGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        _dpiGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        foreach (var _ in Bases)
            _dpiGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (var c = 0; c < Scales.Length; c++)
        {
            var hdr = new TextBlock
            {
                Text = $"{Scales[c]}%",
                FontSize = 11,
                Opacity = 0.7,
                Margin = new Thickness(2),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetRow(hdr, 0);
            Grid.SetColumn(hdr, c + 1);
            _dpiGrid.Children.Add(hdr);
        }

        for (var r = 0; r < Bases.Length; r++)
        {
            var lbl = new TextBlock
            {
                Text = Bases[r].ToString(),
                FontSize = 12,
                FontFamily = MonoFont,
                FontWeight = FontWeight.Bold,
                Opacity = 0.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 8, 0)
            };
            Grid.SetRow(lbl, r + 1);
            Grid.SetColumn(lbl, 0);
            _dpiGrid.Children.Add(lbl);

            for (var c = 0; c < Scales.Length; c++)
            {
                var size = Bases[r] * Scales[c] / 100;
                var vm = GetOrCreateVm(size);
                if (!_dpiCellVms.Contains(vm))
                    _dpiCellVms.Add(vm);
                var btn = MakeCellButton(vm);
                Grid.SetRow(btn, r + 1);
                Grid.SetColumn(btn, c + 1);
                _dpiGrid.Children.Add(btn);
            }
        }
    }

    private static ToggleButton MakeCellButton(SizeCellVm vm)
    {
        var btn = new ToggleButton
        {
            Content = vm.SizeText,
            IsEnabled = !vm.IsReserved
        };
        btn.Classes.Add("cell");
        ToolTip.SetTip(btn, vm.Tooltip);
        btn.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(SizeCellVm.IsChecked))
        {
            Source = vm,
            Mode = BindingMode.TwoWay
        });
        return btn;
    }

    private void PopulateHighRes()
    {
        var vms = new ObservableCollection<SizeCellVm>();
        foreach (var size in HighRes)
            vms.Add(GetOrCreateVm(size));
        _highResItems.ItemsSource = vms;
    }

    private void OnSelectAllToggle()
    {
        var target = !AllDpiChecked();
        foreach (var vm in _dpiCellVms.Where(v => !v.IsReserved))
            vm.IsChecked = target;
    }

    private bool AllDpiChecked()
    {
        return _dpiCellVms.Where(v => !v.IsReserved).All(v => v.IsChecked);
    }

    private void UpdateSelectAllLabel()
    {
        _selectAllBtn.Content = AllDpiChecked() ? "Deselect all" : "Select all";
    }

    private void OnAddCustom()
    {
        var v = (int)Math.Round(_customInput.Value ?? 0m);
        const int min = IconFactory.MinSize;
        const int max = IconFactory.MaxSize;

        if (v is < min or > max)
        {
            SetError($"Must be {min}\u2013{max}.");
            return;
        }

        if (_reserved.Contains(v))
        {
            SetError("Already exists as default.");
            return;
        }

        if (_cellVms.TryGetValue(v, out var vm))
        {
            ClearError();
            vm.IsChecked = true;
            return;
        }

        if (_customRow.Children.OfType<ToggleButton>().Any(b => b.Tag is int t && t == v))
        {
            SetError("Already added.");
            return;
        }

        ClearError();
        AddCustomPill(v);
    }

    private void AddCustomPill(int v)
    {
        var pill = new ToggleButton
        {
            Content = v.ToString(),
            IsChecked = true,
            Tag = v
        };
        pill.Classes.Add("pill");
        ToolTip.SetTip(pill, $"{v}\u00d7{v}  \u2013  custom");
        pill.Click += (_, _) =>
        {
            if (pill.IsChecked == true)
                return;
            _customRow.Children.Remove(pill);
        };

        var insertAt = _customRow.Children.Count;
        for (var i = 0; i < _customRow.Children.Count; i++)
        {
            if (_customRow.Children[i] is not ToggleButton { Tag: int existing } || existing <= v)
                continue;
            insertAt = i;
            break;
        }

        _customRow.Children.Insert(insertAt, pill);
    }

    private void SetError(string text)
    {
        _customError.Text = text;
        _customError.IsVisible = true;
    }

    private void ClearError()
    {
        _customError.IsVisible = false;
    }

    private int[] CollectResult()
    {
        var grid = _cellVms.Values
                           .Where(vm => vm is { IsChecked: true, IsReserved: false })
                           .Select(vm => vm.Size);
        var custom = _customRow.Children.OfType<ToggleButton>()
                               .Where(b => b is { IsChecked: true, Tag: int })
                               .Select(b => (int)b.Tag!);
        return grid.Concat(custom).OrderByDescending(s => s).ToArray();
    }
}