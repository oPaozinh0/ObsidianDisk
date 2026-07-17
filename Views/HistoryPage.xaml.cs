using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ObsidianDisk.Models;
using ObsidianDisk.Services;

namespace ObsidianDisk.Views;

public sealed record HistoryRow(string DateText, string Path, string FilesText, string SizeText,
    string DeltaText, Brush DeltaBrush);

/// <summary>Linha do card "O que mudou": uma pasta e sua variação entre dois scans.</summary>
public sealed record DiffRow(string Name, string Path, string DeltaText, Brush DeltaBrush);

public partial class HistoryPage : UserControl
{
    private List<ScanRecord> _all = new();
    private List<ScanRecord> _filtered = new();
    private bool _updatingCombo;

    public HistoryPage()
    {
        InitializeComponent();
        Reload();
    }

    public void Reload()
    {
        _all = AppStorage.LoadHistory();

        _updatingCombo = true;
        string? previous = (PathCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        PathCombo.Items.Clear();
        foreach (var path in _all.Select(r => r.Path).Distinct(StringComparer.OrdinalIgnoreCase))
            PathCombo.Items.Add(new ComboBoxItem { Content = path, Tag = path });

        int restore = -1;
        if (previous is not null)
            for (int i = 0; i < PathCombo.Items.Count; i++)
                if (string.Equals((string)((ComboBoxItem)PathCombo.Items[i]).Tag!, previous, StringComparison.OrdinalIgnoreCase))
                    restore = i;
        PathCombo.SelectedIndex = restore >= 0 ? restore : PathCombo.Items.Count - 1;
        _updatingCombo = false;

        ApplyFilter();
    }

    private void PathCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingCombo) ApplyFilter();
    }

    private void ApplyFilter()
    {
        string? path = (PathCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        _filtered = path is null
            ? new List<ScanRecord>()
            : _all.Where(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase))
                  .OrderBy(r => r.Timestamp).ToList();

        // Tabela (mais recentes primeiro)
        HistoryList.Items.Clear();
        for (int i = _filtered.Count - 1; i >= 0; i--)
        {
            var r = _filtered[i];
            long delta = i > 0 ? r.TotalBytes - _filtered[i - 1].TotalBytes : 0;
            string deltaText = i == 0 ? "—" :
                (delta >= 0 ? "+" : "−") + FileSystemNode.FormatSize(Math.Abs(delta));
            var deltaBrush = i == 0 ? (Brush)FindResource("Muted")
                : delta > 0 ? new SolidColorBrush(Color.FromRgb(0xE0, 0x6C, 0x75))
                : new SolidColorBrush(Color.FromRgb(0x5F, 0xD3, 0x8A));

            HistoryList.Items.Add(new HistoryRow(
                r.Timestamp.ToString("dd/MM/yyyy HH:mm"),
                r.Path,
                r.FileCount.ToString("N0"),
                FileSystemNode.FormatSize(r.TotalBytes),
                deltaText, deltaBrush));
        }

        ChartHint.Visibility = _filtered.Count >= 2 ? Visibility.Collapsed : Visibility.Visible;
        BuildStats();
        BuildTrend();
        DrawChart();
        BuildDiff(path);
    }

    // ---------------- O que mudou (diff entre snapshots) ----------------

    private void BuildDiff(string? path)
    {
        DiffList.Items.Clear();
        DiffCard.Visibility = Visibility.Collapsed;
        ExplainerBanner.Visibility = Visibility.Collapsed;
        if (path is null) return;

        var (older, newer) = SnapshotStore.TwoLatestForPath(path);
        if (older is null || newer is null) return;

        var deltas = SnapshotStore.Diff(older, newer);
        if (deltas.Count == 0) return;

        // Explicação em linguagem natural do que aconteceu
        var explanation = GrowthExplainer.Explain(older, newer, deltas);
        if (!string.IsNullOrEmpty(explanation))
        {
            ExplainerText.Text = explanation;
            ExplainerBanner.Visibility = Visibility.Visible;
        }

        DiffSubtitle.Text = L.F("Hi.DiffBetween",
            older.Timestamp.ToString("dd/MM HH:mm"), newer.Timestamp.ToString("dd/MM HH:mm"));

        var grow = new SolidColorBrush(Color.FromRgb(0xE0, 0x6C, 0x75));
        var shrink = new SolidColorBrush(Color.FromRgb(0x5F, 0xD3, 0x8A));
        foreach (var d in deltas.Take(12))
            DiffList.Items.Add(new DiffRow(
                d.Name,
                System.IO.Path.GetDirectoryName(d.FullPath) ?? d.FullPath,
                (d.Delta >= 0 ? "+" : "−") + FileSystemNode.FormatSize(Math.Abs(d.Delta)),
                d.Delta > 0 ? grow : shrink));

        DiffCard.Visibility = Visibility.Visible;
    }

    // ---------------- Estatísticas ----------------

    private void BuildStats()
    {
        StatsGrid.Children.Clear();
        if (_filtered.Count < 2) return;

        var min = _filtered.MinBy(r => r.TotalBytes)!;
        var max = _filtered.MaxBy(r => r.TotalBytes)!;
        long avg = (long)_filtered.Average(r => (double)r.TotalBytes);

        long variation = _filtered[^1].TotalBytes - _filtered[0].TotalBytes;
        double days = Math.Max(1 / 24.0, (_filtered[^1].Timestamp - _filtered[0].Timestamp).TotalDays);
        long perMonth = (long)(variation / days * 30);

        long biggestJump = 0;
        DateTime jumpDate = default;
        for (int i = 1; i < _filtered.Count; i++)
        {
            long d = _filtered[i].TotalBytes - _filtered[i - 1].TotalBytes;
            if (d > biggestJump) { biggestJump = d; jumpDate = _filtered[i].Timestamp; }
        }

        AddStat(L.T("Hi.StatMin"), FileSystemNode.FormatSize(min.TotalBytes), min.Timestamp.ToString("dd/MM/yyyy"));
        AddStat(L.T("Hi.StatAvg"), FileSystemNode.FormatSize(avg), L.F("Hi.RecordsCount", _filtered.Count));
        AddStat(L.T("Hi.StatMax"), FileSystemNode.FormatSize(max.TotalBytes), max.Timestamp.ToString("dd/MM/yyyy"));
        AddStat(L.T("Hi.StatTotalChange"), (variation >= 0 ? "+" : "−") + FileSystemNode.FormatSize(Math.Abs(variation)),
            L.F("Hi.OverDays", days.ToString("0.#")), variation > 0);
        AddStat(L.T("Hi.StatRate"), (perMonth >= 0 ? "+" : "−") + FileSystemNode.FormatSize(Math.Abs(perMonth)) + L.T("Hi.PerMonth"),
            L.T("Hi.Extrapolated"), perMonth > 0);
        AddStat(L.T("Hi.StatBiggest"), biggestJump > 0 ? "+" + FileSystemNode.FormatSize(biggestJump) : "—",
            biggestJump > 0 ? jumpDate.ToString("dd/MM/yyyy") : "", biggestJump > 0);
    }

    private void AddStat(string label, string value, string detail, bool warm = false)
    {
        var card = new Border
        {
            Style = (Style)FindResource("Card"),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 10, 10),
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("MutedText") });
        stack.Children.Add(new TextBlock
        {
            Text = value,
            Foreground = warm
                ? new SolidColorBrush(Color.FromRgb(0xE0, 0x8C, 0x5A))
                : (Brush)FindResource("Text"),
            FontSize = 19,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 3, 0, 0),
        });
        stack.Children.Add(new TextBlock
        {
            Text = detail,
            Style = (Style)FindResource("MutedText"),
            FontSize = 11,
        });
        card.Child = stack;
        StatsGrid.Children.Add(card);
    }

    // ---------------- Tendência e projeção ----------------

    private void BuildTrend()
    {
        TrendCard.Visibility = Visibility.Collapsed;
        CapacityWarning.Visibility = Visibility.Collapsed;
        if (_filtered.Count < 2) return;

        var latest = _filtered[^1];

        // Crescimento nos últimos 30 dias (ou desde o registro mais antigo dentro da janela)
        var cutoff = latest.Timestamp.AddDays(-30);
        var baseline = _filtered.FirstOrDefault(r => r.Timestamp >= cutoff) ?? _filtered[0];
        long growth30 = latest.TotalBytes - baseline.TotalBytes;
        Growth30Text.Text = (growth30 >= 0 ? "+" : "−") + FileSystemNode.FormatSize(Math.Abs(growth30));
        Growth30Text.Foreground = growth30 > 0
            ? new SolidColorBrush(Color.FromRgb(0xE0, 0x8C, 0x5A))
            : new SolidColorBrush(Color.FromRgb(0x5F, 0xD3, 0x8A));

        // Ritmo de crescimento (bytes/dia) e projeção de 90 dias — fonte única em DiskForecaster
        double slopePerDay = DiskForecaster.SlopeBytesPerDay(_filtered);
        long projected = latest.TotalBytes + (long)(slopePerDay * 90);
        ProjectionText.Text = FileSystemNode.FormatSize(Math.Max(0, projected));

        // Espaço livre atual + previsão de esgotamento (apenas para raiz de unidade)
        var drive = System.IO.DriveInfo.GetDrives().FirstOrDefault(d =>
            d.IsReady && string.Equals(d.RootDirectory.FullName, latest.Path, StringComparison.OrdinalIgnoreCase));

        if (drive is not null)
        {
            FreeNowText.Text = FileSystemNode.FormatSize(drive.TotalFreeSpace) +
                               $" ({drive.TotalFreeSpace * 100.0 / drive.TotalSize:0.#}%)";

            var forecast = DiskForecaster.Project(_filtered, drive.TotalFreeSpace, DateTime.Now);
            if (forecast is not null)
            {
                CapacityWarningText.Text = L.F("Hi.CapacityWarning",
                    FileSystemNode.FormatSize((long)forecast.SlopeBytesPerDay), forecast.FullDate.ToString("Y"));
                CapacityWarning.Visibility = Visibility.Visible;
            }
        }
        else
        {
            FreeNowText.Text = "—";
        }

        TrendCard.Visibility = Visibility.Visible;
    }

    // ---------------- Exportação ----------------

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_all.Count == 0) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = L.T("Hi.ExportTitle"),
            FileName = $"obsidiandisk-historico-{DateTime.Now:yyyyMMdd}.csv",
            Filter = "CSV (*.csv)|*.csv",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("data;caminho;arquivos;bytes;tamanho");
        foreach (var r in _all.OrderBy(r => r.Timestamp))
            sb.AppendLine($"{r.Timestamp:yyyy-MM-dd HH:mm};{r.Path};{r.FileCount};{r.TotalBytes};{FileSystemNode.FormatSize(r.TotalBytes)}");
        System.IO.File.WriteAllText(dialog.FileName, sb.ToString(), System.Text.Encoding.UTF8);
    }

    private void Chart_SizeChanged(object sender, SizeChangedEventArgs e) => DrawChart();

    private void DrawChart()
    {
        ChartCanvas.Children.Clear();
        double w = ChartCanvas.ActualWidth, h = ChartCanvas.ActualHeight;
        if (_filtered.Count < 2 || w < 60 || h < 40) return;

        long max = _filtered.Max(r => r.TotalBytes);
        long min = _filtered.Min(r => r.TotalBytes);
        long range = Math.Max(1, max - min);
        // margem visual: 12% acima/abaixo
        double pad = h * 0.14;

        var accent = (Brush)FindResource("Accent");
        var stroke = (Brush)FindResource("Stroke");
        var muted = (Brush)FindResource("Muted");

        // linhas de grade
        for (int g = 0; g <= 3; g++)
        {
            double y = pad + (h - 2 * pad) * g / 3.0;
            ChartCanvas.Children.Add(new Line
            {
                X1 = 0, X2 = w, Y1 = y, Y2 = y,
                Stroke = stroke, StrokeThickness = 1, Opacity = 0.5,
            });
            long value = max - (long)(range * g / 3.0);
            var label = new TextBlock
            {
                Text = FileSystemNode.FormatSize(value),
                Foreground = muted, FontSize = 10,
            };
            Canvas.SetLeft(label, 4);
            Canvas.SetTop(label, y - 16);
            ChartCanvas.Children.Add(label);
        }

        Point PointAt(int i)
        {
            double x = _filtered.Count == 1 ? w / 2 : (double)i / (_filtered.Count - 1) * (w - 20) + 10;
            double y = pad + (1 - (_filtered[i].TotalBytes - min) / (double)range) * (h - 2 * pad);
            return new Point(x, y);
        }

        // área sob a linha
        var areaPoints = new PointCollection();
        areaPoints.Add(new Point(PointAt(0).X, h));
        for (int i = 0; i < _filtered.Count; i++) areaPoints.Add(PointAt(i));
        areaPoints.Add(new Point(PointAt(_filtered.Count - 1).X, h));
        ChartCanvas.Children.Add(new Polygon
        {
            Points = areaPoints,
            Fill = new SolidColorBrush(Color.FromArgb(0x28, 0x7C, 0x5C, 0xFF)),
        });

        // linha
        var linePoints = new PointCollection();
        for (int i = 0; i < _filtered.Count; i++) linePoints.Add(PointAt(i));
        ChartCanvas.Children.Add(new Polyline
        {
            Points = linePoints,
            Stroke = accent, StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round,
        });

        // pontos
        for (int i = 0; i < _filtered.Count; i++)
        {
            var p = PointAt(i);
            var dot = new Ellipse { Width = 7, Height = 7, Fill = accent };
            Canvas.SetLeft(dot, p.X - 3.5);
            Canvas.SetTop(dot, p.Y - 3.5);
            dot.ToolTip = $"{_filtered[i].Timestamp:dd/MM/yyyy HH:mm} — {FileSystemNode.FormatSize(_filtered[i].TotalBytes)}";
            ChartCanvas.Children.Add(dot);
        }

        // datas nas extremidades do eixo X
        var firstLabel = new TextBlock
        {
            Text = _filtered[0].Timestamp.ToString("dd/MM/yy"),
            Foreground = muted, FontSize = 10,
        };
        Canvas.SetLeft(firstLabel, 10);
        Canvas.SetTop(firstLabel, h - 14);
        ChartCanvas.Children.Add(firstLabel);

        var lastLabel = new TextBlock
        {
            Text = _filtered[^1].Timestamp.ToString("dd/MM/yy"),
            Foreground = muted, FontSize = 10,
        };
        lastLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(lastLabel, w - lastLabel.DesiredSize.Width - 10);
        Canvas.SetTop(lastLabel, h - 14);
        ChartCanvas.Children.Add(lastLabel);
    }
}
