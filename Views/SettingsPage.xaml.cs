using System.Windows;
using System.Windows.Controls;
using ObsidianDisk.Services;

namespace ObsidianDisk.Views;

public partial class SettingsPage : UserControl
{
    private static readonly (string Label, long Bytes)[] MinSizes =
    {
        ("10 MB", 10L << 20),
        ("50 MB", 50L << 20),
        ("100 MB", 100L << 20),
        ("500 MB", 500L << 20),
        ("1 GB", 1L << 30),
    };

    private AppSettings _settings = new();
    private bool _loading = true;

    public event Action<AppSettings>? SettingsChanged;

    public SettingsPage()
    {
        InitializeComponent();
        foreach (var (label, bytes) in MinSizes)
            MinSizeCombo.Items.Add(new ComboBoxItem { Content = label, Tag = bytes });
    }

    public void Load(AppSettings settings)
    {
        _loading = true;
        _settings = settings;
        ConfirmDeleteSwitch.IsChecked = settings.ConfirmDelete;

        int index = Array.FindIndex(MinSizes, m => m.Bytes == settings.LargeFileMinBytes);
        MinSizeCombo.SelectedIndex = index >= 0 ? index : 2;
        _loading = false;
    }

    private void Setting_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        _settings.ConfirmDelete = ConfirmDeleteSwitch.IsChecked == true;
        if (MinSizeCombo.SelectedItem is ComboBoxItem item && item.Tag is long bytes)
            _settings.LargeFileMinBytes = bytes;

        AppStorage.SaveSettings(_settings);
        SettingsChanged?.Invoke(_settings);
    }
}
