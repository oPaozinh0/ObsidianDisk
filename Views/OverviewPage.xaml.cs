using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using ObsidianDisk.Models;
using ObsidianDisk.Services;

namespace ObsidianDisk.Views;

public partial class OverviewPage : UserControl
{
    private string? _customFolder;

    public event Action<string>? ScanRequested;
    public event Action<FileSystemNode>? OpenInMapRequested;
    public event Action? OpenCleanupRequested;
    public event Action? OpenLargeFilesRequested;
    public event Action? OpenDuplicatesRequested;

    public OverviewPage()
    {
        InitializeComponent();
        LoadDrives();
        UpdateWelcomeButton();
    }

    private void UpdateWelcomeButton()
    {
        string label = _customFolder ?? ((DriveCombo.SelectedItem as ComboBoxItem)?.Tag as string)?.TrimEnd('\\') ?? "C:";
        WelcomeScanButton.Content = L.F("Ov.WelcomeButton", label);
    }

    private void RecCleanup_Click(object sender, RoutedEventArgs e) => OpenCleanupRequested?.Invoke();
    private void RecLarge_Click(object sender, RoutedEventArgs e) => OpenLargeFilesRequested?.Invoke();
    private void RecDup_Click(object sender, RoutedEventArgs e) => OpenDuplicatesRequested?.Invoke();

    // ---------------- Recomendações ----------------

    private async void UpdateRecommendations(FileSystemNode root)
    {
        if (RecommendationsCard.Visibility != Visibility.Visible)
            Controls.Animate.FadeIn(RecommendationsCard, 260);

        // Arquivos gigantes (500 MB+) direto da árvore escaneada
        const long huge = 500L * 1024 * 1024;
        long hugeTotal = 0;
        int hugeCount = 0;
        CountHuge(root);

        void CountHuge(FileSystemNode dir)
        {
            var children = dir.Children;
            int count = children.Count;
            for (int i = 0; i < count; i++)
            {
                var child = children[i];
                if (child.IsDirectory) CountHuge(child);
                else if (child.Size >= huge) { hugeCount++; hugeTotal += child.Size; }
            }
        }

        RecLargeText.Text = hugeCount > 0
            ? L.F("Ov.RecLargeValue", hugeCount, FileSystemNode.FormatSize(hugeTotal))
            : L.T("Ov.RecLargeNone");

        // Temporários + Lixeira medidos em background
        RecTempText.Text = L.T("Ov.RecTempMeasuring");
        long tempTotal = await Task.Run(() =>
            TempCleaner.GetTargets()
                .Where(t => t.Key is "user-temp" or "win-temp" || t.IsRecycleBin)
                .Sum(TempCleaner.Measure));
        RecTempText.Text = L.F("Ov.RecTempValue", FileSystemNode.FormatSize(tempTotal));
    }

    public string? SelectedPath =>
        _customFolder ?? (DriveCombo.SelectedItem as ComboBoxItem)?.Tag as string;

    public void SetScanning(bool scanning) =>
        ScanButton.Content = scanning ? L.T("Ov.CancelScan") : L.T("Ov.Scan");

    private void LoadDrives()
    {
        DriveCombo.Items.Clear();
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            DriveCombo.Items.Add(new ComboBoxItem
            {
                Content = $"{drive.Name}  ·  {FileSystemNode.FormatSize(drive.TotalSize)}",
                Tag = drive.RootDirectory.FullName,
            });
        }
        if (DriveCombo.Items.Count > 0)
            DriveCombo.SelectedIndex = 0;
        UpdateDiskCard();
    }

    private void DriveCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _customFolder = null;
        UpdateDiskCard();
        UpdateWelcomeButton();
    }

    private void PickFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = L.T("Ov.PickFolderTitle") };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            _customFolder = dialog.FolderName;
            UpdateDiskCard();
        }
    }

    private void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPath is { } path)
            ScanRequested?.Invoke(path);
    }

    /// <summary>Define a pasta manualmente (ex.: vinda de argumento de linha de comando).</summary>
    public void SetCustomFolder(string path)
    {
        _customFolder = path;
        UpdateDiskCard();
        UpdateWelcomeButton();
    }

    public void UpdateDiskCard()
    {
        var path = SelectedPath;
        if (path is null) return;

        var drive = DriveInfo.GetDrives().FirstOrDefault(d =>
            d.IsReady && string.Equals(d.RootDirectory.FullName, Path.GetPathRoot(path), StringComparison.OrdinalIgnoreCase));

        if (_customFolder is not null)
        {
            DiskTitle.Text = _customFolder;
            DiskSubtitle.Text = L.T("Ov.CustomFolder");
        }
        else if (drive is not null)
        {
            DiskTitle.Text = L.F("Ov.DiskLocal", drive.Name.TrimEnd('\\'));
            DiskSubtitle.Text = $"{FileSystemNode.FormatSize(drive.TotalSize)} · {drive.DriveFormat}";
        }

        if (drive is not null)
        {
            long used = drive.TotalSize - drive.TotalFreeSpace;
            double percent = used * 100.0 / drive.TotalSize;

            // Anima só quando o valor muda de verdade (não a cada refresh do monitoramento)
            if (Math.Abs(UsageBar.Value - percent) > 0.05)
            {
                Controls.Animate.ProgressTo(UsageBar, percent);
                Controls.Animate.CountTo(PercentText, 0, percent, v => $"{v:0}%");
            }
            UsageText.Text = L.F("Ov.UsedOf", FileSystemNode.FormatSize(used), FileSystemNode.FormatSize(drive.TotalSize));
            FreeText.Text = L.F("Ov.Free", FileSystemNode.FormatSize(drive.TotalFreeSpace));
        }
    }

    // ---------------- Blocos semânticos (Mapa de Espaço) ----------------

    private void BuildBuckets(FileSystemNode root)
    {
        var buckets = SemanticGrouper.Group(root);
        long total = Math.Max(1, root.Size);

        BigBucketsGrid.Children.Clear();
        SmallBucketsGrid.Children.Clear();
        BucketsHint.Text = L.F("Ov.Mapped", FileSystemNode.FormatSize(root.Size));

        for (int i = 0; i < buckets.Count; i++)
        {
            var bucket = buckets[i];
            bool big = i < 3; // os 3 maiores ganham cartões grandes, como no mockup
            var card = BuildBucketCard(bucket, total, big);
            (big ? BigBucketsGrid : SmallBucketsGrid).Children.Add(card);
        }
    }

    private FrameworkElement BuildBucketCard(SpaceBucket bucket, long total, bool big)
    {
        var c = bucket.Color;

        var border = new Border
        {
            CornerRadius = new CornerRadius(12),
            Margin = new Thickness(0, 0, 10, 10),
            Padding = new Thickness(16, big ? 14 : 10, 16, big ? 14 : 10),
            MinHeight = big ? 130 : 84,
            Background = new LinearGradientBrush(
                Color.FromArgb(0x55, c.R, c.G, c.B),
                Color.FromArgb(0x2A, c.R, c.G, c.B), 90),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x88, c.R, c.G, c.B)),
            BorderThickness = new Thickness(1),
            Cursor = bucket.PrimaryNode is not null ? Cursors.Hand : Cursors.Arrow,
        };

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        stack.Children.Add(new TextBlock
        {
            Text = bucket.Glyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = big ? 22 : 16,
            Foreground = new SolidColorBrush(Color.FromArgb(0xE6, c.R, c.G, c.B)),
            Margin = new Thickness(0, 0, 0, big ? 10 : 6),
        });
        stack.Children.Add(new TextBlock
        {
            Text = bucket.Label,
            Foreground = (Brush)FindResource("Text"),
            FontSize = big ? 15 : 13,
            FontWeight = FontWeights.SemiBold,
        });
        stack.Children.Add(new TextBlock
        {
            Text = FileSystemNode.FormatSize(bucket.Size),
            Foreground = (Brush)FindResource("Text"),
            FontSize = big ? 20 : 15,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 2, 0, 0),
        });
        stack.Children.Add(new TextBlock
        {
            Text = $"{bucket.Size * 100.0 / total:0.#}%",
            Foreground = (Brush)FindResource("Muted"),
            FontSize = 12,
        });

        border.Child = stack;

        if (bucket.PrimaryNode is { } node)
        {
            border.MouseLeftButtonUp += (_, _) => OpenInMapRequested?.Invoke(node);
            border.MouseEnter += (_, _) => border.BorderBrush = new SolidColorBrush(c);
            border.MouseLeave += (_, _) =>
                border.BorderBrush = new SolidColorBrush(Color.FromArgb(0x88, c.R, c.G, c.B));
        }

        return border;
    }

    /// <summary>Atualiza blocos semânticos, categorias e maiores pastas a partir do scan concluído.</summary>
    public void UpdateFromScan(FileSystemNode root)
    {
        bool firstTime = WelcomeBanner.Visibility == Visibility.Visible;
        WelcomeBanner.Visibility = Visibility.Collapsed;
        UpdateDiskCard();
        BuildBuckets(root);
        UpdateRecommendations(root);

        // ---- Categorias ----
        var totals = FileCategories.Aggregate(root);
        long grandTotal = Math.Max(1, root.Size);

        CategoryGrid.Children.Clear();
        foreach (var cat in FileCategories.All)
        {
            long size = totals[cat];
            var color = FileCategories.ColorOf(cat);

            var card = new Border
            {
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(12, 10, 12, 10),
                Background = new SolidColorBrush(Color.FromArgb(0x30, color.R, color.G, color.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x70, color.R, color.G, color.B)),
                BorderThickness = new Thickness(1),
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = FileCategories.LabelOf(cat),
                Foreground = (Brush)FindResource("Text"),
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
            });
            stack.Children.Add(new TextBlock
            {
                Text = FileSystemNode.FormatSize(size),
                Foreground = new SolidColorBrush(color),
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 4, 0, 0),
            });
            stack.Children.Add(new TextBlock
            {
                Text = $"{size * 100.0 / grandTotal:0.#}%",
                Foreground = (Brush)FindResource("Muted"),
                FontSize = 11,
            });
            card.Child = stack;
            CategoryGrid.Children.Add(card);
        }
        CategoriesHint.Text = L.F("Ov.OfScanned", FileSystemNode.FormatSize(root.Size));

        // ---- Maiores pastas ----
        TopFoldersPanel.Children.Clear();
        var topDirs = root.Children.Where(c => c.IsDirectory && c.Size > 0)
                          .OrderByDescending(c => c.Size).Take(6).ToList();
        TopFoldersHint.Visibility = topDirs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var dir in topDirs)
        {
            double percent = dir.Size * 100.0 / grandTotal;

            var row = new Button
            {
                Style = (Style)FindResource("CrumbButton"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 2, 0, 2),
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });

            var name = new TextBlock
            {
                Text = dir.Name,
                Foreground = (Brush)FindResource("Text"),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(name, 0);

            var sizeText = new TextBlock
            {
                Text = FileSystemNode.FormatSize(dir.Size),
                Foreground = (Brush)FindResource("Text"),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 16, 0),
            };
            Grid.SetColumn(sizeText, 1);

            var barBack = new Border
            {
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = (Brush)FindResource("Panel2"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var barFill = new Border
            {
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = (Brush)FindResource("Accent"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = Math.Max(3, percent * 2), // 200px de largura total
            };
            var barHost = new Grid();
            barHost.Children.Add(barBack);
            barHost.Children.Add(barFill);
            barHost.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(barHost, 2);

            grid.Children.Add(name);
            grid.Children.Add(sizeText);
            grid.Children.Add(barHost);
            row.Content = grid;

            row.Click += (_, _) => OpenInMapRequested?.Invoke(dir);
            TopFoldersPanel.Children.Add(row);
        }
    }
}
