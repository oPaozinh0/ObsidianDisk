using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using ObsidianDisk.Services;

namespace ObsidianDisk.Views;

public partial class SettingsPage : UserControl
{
    private const string CoffeeUrl = "https://buymeacoffee.com/obsidiandisk";
    private const string LivePixUrl = "https://livepix.gg/opaozinh0";
    private const string GitHubUrl = "https://github.com/oPaozinh0/ObsidianDisk";

    private static readonly (string Label, long Bytes)[] MinSizes =
    {
        ("10 MB", 10L << 20),
        ("50 MB", 50L << 20),
        ("100 MB", 100L << 20),
        ("500 MB", 500L << 20),
        ("1 GB", 1L << 30),
    };

    private static readonly (string Code, string Name)[] Languages =
    {
        ("auto", ""), // rótulo vem do recurso St.LanguageAuto
        ("pt", "Português (Brasil)"),
        ("en", "English"),
        ("es", "Español"),
        ("fr", "Français"),
        ("de", "Deutsch"),
    };

    private AppSettings _settings = new();
    private bool _loading = true;

    public event Action<AppSettings>? SettingsChanged;

    public SettingsPage()
    {
        InitializeComponent();
        foreach (var (label, bytes) in MinSizes)
            MinSizeCombo.Items.Add(new ComboBoxItem { Content = label, Tag = bytes });

        foreach (var (code, name) in Languages)
            LanguageCombo.Items.Add(new ComboBoxItem
            {
                Content = code == "auto" ? L.T("St.LanguageAuto") : name,
                Tag = code,
            });

        ThemeCombo.Items.Add(new ComboBoxItem { Content = L.T("St.ThemeDark"), Tag = "dark" });
        ThemeCombo.Items.Add(new ComboBoxItem { Content = L.T("St.ThemeLight"), Tag = "light" });

        var v = typeof(SettingsPage).Assembly.GetName().Version;
        VersionText.Text = $"ObsidianDisk {v?.ToString(3) ?? ""}";
    }

    public void Load(AppSettings settings)
    {
        _loading = true;
        _settings = settings;
        ConfirmDeleteSwitch.IsChecked = settings.ConfirmDelete;
        LiveMonitoringSwitch.IsChecked = settings.LiveMonitoring;

        int index = Array.FindIndex(MinSizes, m => m.Bytes == settings.LargeFileMinBytes);
        MinSizeCombo.SelectedIndex = index >= 0 ? index : 2;

        int langIndex = Array.FindIndex(Languages, l =>
            l.Code.Equals(settings.Language, StringComparison.OrdinalIgnoreCase));
        LanguageCombo.SelectedIndex = langIndex >= 0 ? langIndex : 0;

        ThemeCombo.SelectedIndex = settings.Theme.Equals("light", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        ColorBlindSwitch.IsChecked = settings.ColorBlindSafe;
        SoftwareRenderSwitch.IsChecked = settings.SoftwareRendering;
        MinimizeToTraySwitch.IsChecked = settings.MinimizeToTray;
        _loading = false;
    }

    private void Setting_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        _settings.ConfirmDelete = ConfirmDeleteSwitch.IsChecked == true;
        _settings.LiveMonitoring = LiveMonitoringSwitch.IsChecked == true;
        _settings.MinimizeToTray = MinimizeToTraySwitch.IsChecked == true;
        if (MinSizeCombo.SelectedItem is ComboBoxItem item && item.Tag is long bytes)
            _settings.LargeFileMinBytes = bytes;

        AppStorage.SaveSettings(_settings);
        SettingsChanged?.Invoke(_settings);
    }

    private void Interface_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        string lang = (string)(((ComboBoxItem)LanguageCombo.SelectedItem).Tag ?? "auto");
        string theme = (string)(((ComboBoxItem)ThemeCombo.SelectedItem).Tag ?? "dark");
        bool colorBlind = ColorBlindSwitch.IsChecked == true;
        bool software = SoftwareRenderSwitch.IsChecked == true;

        // Tema e daltonismo valem na hora. Idioma e renderização precisam reiniciar:
        // o idioma porque as strings já estão espalhadas pela tela; a renderização
        // porque o WPF fixa o motor (GPU/software) na criação da janela.
        if (theme != _settings.Theme)
            App.ApplyTheme(theme.Equals("light", StringComparison.OrdinalIgnoreCase));
        if (colorBlind != _settings.ColorBlindSafe)
            App.ApplyColorBlind(colorBlind);

        bool needsRestart = lang != _settings.Language || software != _settings.SoftwareRendering;

        _settings.Language = lang;
        _settings.Theme = theme;
        _settings.ColorBlindSafe = colorBlind;
        _settings.SoftwareRendering = software;
        AppStorage.SaveSettings(_settings);
        SettingsChanged?.Invoke(_settings);

        if (needsRestart && Controls.DarkDialog.Confirm(Window.GetWindow(this)!,
                L.T("St.RestartTitle"), L.T("St.RestartMsg"),
                confirmLabel: L.T("St.RestartNow"), cancelLabel: L.T("St.RestartLater")))
            App.Restart();
    }

    private void BuyCoffee_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(CoffeeUrl) { UseShellExecute = true });

    private void LivePix_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(LivePixUrl) { UseShellExecute = true });

    private void GitHub_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(GitHubUrl) { UseShellExecute = true });
}
