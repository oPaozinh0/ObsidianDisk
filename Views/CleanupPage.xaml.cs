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
    private bool _measured;

    public CleanupPage()
    {
        InitializeComponent();
        BuildRows();
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
            check.Checked += (_, _) => UpdateCleanButton();
            check.Unchecked += (_, _) => UpdateCleanButton();
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

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await MeasureAllAsync();

    private async Task MeasureAllAsync()
    {
        if (_busy) return;
        _busy = true;
        RefreshButton.IsEnabled = false;
        ProgressRow.Visibility = Visibility.Visible;

        foreach (var row in _rows)
        {
            ProgressLabel.Text = $"Medindo: {row.Target.Label}…";
            row.Size = await Task.Run(() => TempCleaner.Measure(row.Target));
            row.SizeText.Text = row.Size > 0 ? FileSystemNode.FormatSize(row.Size) : "vazio";
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
            ? $"🧹  Limpar {FileSystemNode.FormatSize(selected)}"
            : "🧹  Limpar selecionados";
    }

    private async void Clean_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        var selected = _rows.Where(r => r.Check.IsChecked == true).ToList();
        if (selected.Count == 0)
        {
            ResultBanner.Visibility = Visibility.Visible;
            ResultText.Text = "Marque ao menos um item para limpar.";
            return;
        }

        long estimate = selected.Sum(r => r.Size);
        bool hasRecycle = selected.Any(r => r.Target.IsRecycleBin);
        string warning = $"Limpar {selected.Count} local(is), liberando cerca de {FileSystemNode.FormatSize(estimate)}?" +
                         "\n\nOs arquivos temporários são removidos DEFINITIVAMENTE (não vão para a Lixeira)." +
                         (hasRecycle ? "\nA Lixeira será esvaziada — os itens dela não poderão ser restaurados." : "");

        if (!Controls.DarkDialog.Confirm(Window.GetWindow(this)!, "Confirmar limpeza", warning,
                danger: true, confirmLabel: "Limpar agora", cancelLabel: "Cancelar"))
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
            ProgressLabel.Text = $"Limpando: {row.Target.Label}…";
            var result = await Task.Run(() => TempCleaner.Clean(row.Target, CancellationToken.None));
            totalFreed += result.FreedBytes;
            totalFailed += result.FailedItems;
        }

        ProgressRow.Visibility = Visibility.Collapsed;
        CleanButton.IsEnabled = true;
        RefreshButton.IsEnabled = true;
        _busy = false;

        ResultBanner.Visibility = Visibility.Visible;
        ResultText.Text = $"✅  Limpeza concluída: {FileSystemNode.FormatSize(totalFreed)} liberados." +
                          (totalFailed > 0
                              ? $" {totalFailed} item(ns) em uso ou protegidos foram pulados" +
                                " — executar como administrador pode liberar mais."
                              : "");

        await MeasureAllAsync();
    }
}
