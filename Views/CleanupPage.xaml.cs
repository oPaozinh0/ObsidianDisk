using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ObsidianDisk.Models;
using ObsidianDisk.Services;

namespace ObsidianDisk.Views;

public partial class CleanupPage : UserControl
{
    private sealed record TargetRow(TempTarget Target, CheckBox Check, TextBlock SizeText)
    {
        public long Size { get; set; }
    }

    private readonly List<TargetRow> _rows = new();
    private bool _busy;

    /// <summary>Disparado após uma limpeza real (para atualizar a gamificação).</summary>
    public event Action? CleanupCompleted;
    private bool _measured;
    private bool _initializing = true;
    private bool _applyingProfile;

    // Perfil conservador: só o que regenera sozinho, sem re-download nem esvaziar a Lixeira.
    // Agressivo = todos os alvos (inclui cache do Windows Update e Lixeira).
    private static readonly HashSet<string> ConservativeKeys =
        new(StringComparer.OrdinalIgnoreCase) { "user-temp", "win-temp", "thumbs", "wer" };

    public static bool IsAdmin
    {
        get
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
    }

    public CleanupPage()
    {
        InitializeComponent();
        BuildRows();
        ElevateButton.Visibility = IsAdmin ? Visibility.Collapsed : Visibility.Visible;

        ProfileCombo.Items.Add(new ComboBoxItem { Content = L.T("Cl.ProfileConservative"), Tag = "conservative" });
        ProfileCombo.Items.Add(new ComboBoxItem { Content = L.T("Cl.ProfileAggressive"), Tag = "aggressive" });
        ProfileCombo.Items.Add(new ComboBoxItem { Content = L.T("Cl.ProfileCustom"), Tag = "custom" });
        _initializing = false;
        ProfileCombo.SelectedIndex = 0; // começa no conservador (seguro)
    }

    private void Profile_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        var tag = (string)(((ComboBoxItem)ProfileCombo.SelectedItem).Tag ?? "custom");
        UpdateProfileHint(tag);
        if (tag == "custom") return; // "Personalizado" reflete edição manual, não altera a seleção
        ApplyProfile(tag);
    }

    private void ApplyProfile(string tag)
    {
        _applyingProfile = true;
        foreach (var row in _rows)
            row.Check.IsChecked = tag == "aggressive" || ConservativeKeys.Contains(row.Target.Key);
        _applyingProfile = false;
        UpdateCleanButton();
    }

    private void UpdateProfileHint(string tag) => ProfileHint.Text = tag switch
    {
        "conservative" => L.T("Cl.ProfileConservativeHint"),
        "aggressive" => L.T("Cl.ProfileAggressiveHint"),
        _ => "",
    };

    /// <summary>Seleciona um perfil sem disparar a aplicação (usado ao detectar edição manual).</summary>
    private void SelectProfile(string tag)
    {
        foreach (ComboBoxItem item in ProfileCombo.Items)
            if ((string?)item.Tag == tag) { ProfileCombo.SelectedItem = item; return; }
    }

    private void Elevate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Reinicia o próprio exe com elevação (UAC)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = Environment.ProcessPath!,
                UseShellExecute = true,
                Verb = "runas",
            });
            Application.Current.Shutdown();
        }
        catch
        {
            // usuário cancelou o UAC — segue normalmente
        }
    }

    /// <summary>Mede os tamanhos na primeira vez que a página é exibida.</summary>
    public async void EnsureMeasured()
    {
        if (_measured || _busy) return;
        _measured = true;
        await MeasureAllAsync();
    }

    private void BuildRows()
    {
        foreach (var target in TempCleaner.GetTargets())
        {
            var row = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(4, 3, 4, 3),
                Background = (Brush)FindResource("Panel2"),
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });

            var check = new CheckBox
            {
                Style = (Style)FindResource("DarkCheck"),
                IsChecked = target.DefaultChecked,
                VerticalAlignment = VerticalAlignment.Center,
            };
            check.Checked += Row_CheckChanged;
            check.Unchecked += Row_CheckChanged;
            Grid.SetColumn(check, 0);

            var textStack = new StackPanel { Margin = new Thickness(10, 0, 10, 0) };
            textStack.Children.Add(new TextBlock
            {
                Text = target.Label,
                Foreground = (Brush)FindResource("Text"),
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
            });
            textStack.Children.Add(new TextBlock
            {
                Text = target.Description,
                Foreground = (Brush)FindResource("Muted"),
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
            });
            Grid.SetColumn(textStack, 1);

            var sizeText = new TextBlock
            {
                Text = "—",
                Foreground = (Brush)FindResource("Text"),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            Grid.SetColumn(sizeText, 2);

            grid.Children.Add(check);
            grid.Children.Add(textStack);
            grid.Children.Add(sizeText);
            row.Child = grid;
            TargetsPanel.Children.Add(row);

            _rows.Add(new TargetRow(target, check, sizeText));
        }
    }

    /// <summary>Uma marcação manual muda o perfil para "Personalizado".</summary>
    private void Row_CheckChanged(object sender, RoutedEventArgs e)
    {
        if (!_applyingProfile) SelectProfile("custom");
        UpdateCleanButton();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await MeasureAllAsync();

    private async Task MeasureAllAsync()
    {
        if (_busy) return;
        _busy = true;
        RefreshButton.IsEnabled = false;
        ProgressRow.Visibility = Visibility.Visible;

        foreach (var row in _rows)
        {
            ProgressLabel.Text = L.F("Cl.Measuring", row.Target.Label);
            row.Size = await Task.Run(() => TempCleaner.Measure(row.Target));
            row.SizeText.Text = row.Size > 0 ? FileSystemNode.FormatSize(row.Size) : L.T("Cl.Empty");
            row.SizeText.Foreground = row.Size > 0
                ? (Brush)FindResource("Text")
                : (Brush)FindResource("Muted");
        }

        ProgressRow.Visibility = Visibility.Collapsed;
        RefreshButton.IsEnabled = true;
        _busy = false;
        UpdateCleanButton();
    }

    private void UpdateCleanButton()
    {
        long selected = _rows.Where(r => r.Check.IsChecked == true).Sum(r => r.Size);
        CleanButton.Content = selected > 0
            ? L.F("Cl.CleanAmount", FileSystemNode.FormatSize(selected))
            : L.T("Cl.Clean");
    }

    private async void Clean_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        var selected = _rows.Where(r => r.Check.IsChecked == true).ToList();
        if (selected.Count == 0)
        {
            ResultBanner.Visibility = Visibility.Visible;
            ResultText.Text = L.T("Cl.SelectOne");
            return;
        }

        long estimate = selected.Sum(r => r.Size);

        // Simulação: mostra o que seria liberado, sem deletar nada
        if (DryRunCheck.IsChecked == true)
        {
            ResultBanner.Visibility = Visibility.Visible;
            ResultText.Text = L.F("Cl.DryRunResult", selected.Count, FileSystemNode.FormatSize(estimate));
            return;
        }

        bool hasRecycle = selected.Any(r => r.Target.IsRecycleBin);
        string warning = L.F("Cl.ConfirmMsg", selected.Count, FileSystemNode.FormatSize(estimate)) +
                         (hasRecycle ? L.T("Cl.ConfirmRecycleNote") : "");

        if (!Controls.DarkDialog.Confirm(Window.GetWindow(this)!, L.T("Cl.ConfirmTitle"), warning,
                danger: true, confirmLabel: L.T("Cl.ConfirmBtn"), cancelLabel: L.T("Del.Cancel")))
            return;

        _busy = true;
        CleanButton.IsEnabled = false;
        RefreshButton.IsEnabled = false;
        ProgressRow.Visibility = Visibility.Visible;
        ResultBanner.Visibility = Visibility.Collapsed;

        long totalFreed = 0;
        int totalFailed = 0;

        foreach (var row in selected)
        {
            ProgressLabel.Text = L.F("Cl.Cleaning", row.Target.Label);
            var result = await Task.Run(() => TempCleaner.Clean(row.Target, CancellationToken.None));
            totalFreed += result.FreedBytes;
            totalFailed += result.FailedItems;
        }

        ProgressRow.Visibility = Visibility.Collapsed;
        CleanButton.IsEnabled = true;
        RefreshButton.IsEnabled = true;
        _busy = false;

        ResultBanner.Visibility = Visibility.Visible;
        ResultText.Text = L.F("Cl.Done", FileSystemNode.FormatSize(totalFreed)) +
                          (totalFailed > 0 ? L.F("Cl.DoneSkipped", totalFailed) : "");

        if (totalFreed > 0)
        {
            StatsStore.Record(totalFreed); // gamificação: total recuperado + sequência
            CleanupCompleted?.Invoke();
        }

        await MeasureAllAsync();
    }
}
