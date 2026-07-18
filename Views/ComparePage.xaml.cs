using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ObsidianDisk.Models;
using ObsidianDisk.Services;

namespace ObsidianDisk.Views;

/// <summary>
/// Compara duas pastas lado a lado: tamanho total, nº de arquivos e a quebra por categoria.
/// Reaproveita a árvore do scan quando a pasta está dentro dela; senão, faz um scan rápido.
/// </summary>
public partial class ComparePage : UserControl
{
    private FileSystemNode? _root;
    private string? _pathA;
    private string? _pathB;
    private bool _busy;

    public ComparePage() => InitializeComponent();

    public void UpdateFromScan(FileSystemNode? root) => _root = root;

    private void PickA_Click(object sender, RoutedEventArgs e) => Pick(ref _pathA, PickAButton);
    private void PickB_Click(object sender, RoutedEventArgs e) => Pick(ref _pathB, PickBButton);

    private void Pick(ref string? slot, Controls.ObsidianButton button)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = L.T("Cmp.PickTitle"),
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        slot = dlg.SelectedPath;
        button.Content = Shorten(slot);
    }

    private static string Shorten(string path) =>
        path.Length <= 40 ? path : "…" + path[^39..];

    private async void Compare_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (_pathA is null || _pathB is null)
        {
            BusyText.Text = L.T("Cmp.PickBoth");
            return;
        }

        _busy = true;
        BusyText.Text = L.T("Cmp.Working");
        ColumnA.Children.Clear();
        ColumnB.Children.Clear();

        var nodeA = await GetNodeAsync(_pathA);
        var nodeB = await GetNodeAsync(_pathB);

        _busy = false;
        BusyText.Text = "";

        if (nodeA is null || nodeB is null)
        {
            BusyText.Text = L.T("Cmp.NotFound");
            return;
        }

        RenderColumn(ColumnA, _pathA, nodeA, nodeB.Size);
        RenderColumn(ColumnB, _pathB, nodeB, nodeA.Size);
    }

    /// <summary>Pega o nó da árvore já escaneada; se a pasta estiver fora dela, faz um scan próprio.</summary>
    private async Task<FileSystemNode?> GetNodeAsync(string path)
    {
        if (_root?.FindByPath(path) is { } inTree) return inTree;
        if (!Directory.Exists(path)) return null;

        var scanner = new DiskScanner();
        var (root, task) = scanner.StartScan(path, CancellationToken.None);
        await task;
        root.SortBySizeDescending();
        return root;
    }

    private void RenderColumn(StackPanel panel, string path, FileSystemNode node, long otherTotal)
    {
        var card = new Border
        {
            Style = (Style)FindResource("Card"),
            Padding = new Thickness(16, 14, 16, 14),
        };
        var stack = new StackPanel();

        stack.Children.Add(new TextBlock
        {
            Text = path,
            Foreground = (Brush)FindResource("Text"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });

        stack.Children.Add(new TextBlock
        {
            Text = FileSystemNode.FormatSize(node.Size),
            Foreground = (Brush)FindResource("Text"),
            FontSize = 26,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 8, 0, 0),
        });

        // Diferença em relação à outra pasta
        long diff = node.Size - otherTotal;
        stack.Children.Add(new TextBlock
        {
            Text = diff == 0 ? L.T("Cmp.Equal")
                 : diff > 0 ? L.F("Cmp.Bigger", FileSystemNode.FormatSize(diff))
                 : L.F("Cmp.Smaller", FileSystemNode.FormatSize(-diff)),
            Foreground = diff > 0
                ? new SolidColorBrush(Color.FromRgb(0xE0, 0x8C, 0x5A))
                : (Brush)FindResource("Muted"),
            FontSize = 12,
            Margin = new Thickness(0, 2, 0, 0),
        });

        var (files, cats) = ComputeStats(node);
        stack.Children.Add(new TextBlock
        {
            Text = L.F("Cmp.Files", files.ToString("N0")),
            Style = (Style)FindResource("MutedText"),
            Margin = new Thickness(0, 6, 0, 10),
        });

        long max = Math.Max(1, node.Size);
        foreach (var (cat, size) in cats)
            stack.Children.Add(CategoryRow(cat, size, max));

        card.Child = stack;
        panel.Children.Add(card);
    }

    private FrameworkElement CategoryRow(FileCategory cat, long size, long max)
    {
        var row = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

        var header = new DockPanel { LastChildFill = false };
        var dot = new Border
        {
            Width = 9, Height = 9, CornerRadius = new CornerRadius(4.5),
            Background = new SolidColorBrush(FileCategories.ColorOf(cat)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0),
        };
        DockPanel.SetDock(dot, Dock.Left);
        header.Children.Add(dot);

        var label = new TextBlock
        {
            Text = FileCategories.LabelOf(cat),
            Foreground = (Brush)FindResource("Text"),
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(label, Dock.Left);
        header.Children.Add(label);

        var sizeText = new TextBlock
        {
            Text = FileSystemNode.FormatSize(size),
            Foreground = (Brush)FindResource("Muted"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(sizeText, Dock.Right);
        header.Children.Add(sizeText);

        row.Children.Add(header);

        // Barra proporcional ao total da pasta
        var track = new Border
        {
            Height = 6, CornerRadius = new CornerRadius(3),
            Background = (Brush)FindResource("Panel2"),
            Margin = new Thickness(0, 4, 0, 0),
        };
        var fill = new Border
        {
            Height = 6, CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(FileCategories.ColorOf(cat)),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        track.Child = fill;
        // A largura do trilho só é conhecida no layout; recalcula sempre que muda
        track.SizeChanged += (_, _) => fill.Width = Math.Max(2, track.ActualWidth * size / max);
        row.Children.Add(track);

        return row;
    }

    private static (int Files, List<(FileCategory Cat, long Size)> Categories) ComputeStats(FileSystemNode node)
    {
        int files = 0;
        var byCat = new Dictionary<FileCategory, long>();

        void Walk(FileSystemNode n)
        {
            foreach (var child in n.Children)
            {
                if (child.IsDirectory) { Walk(child); continue; }
                files++;
                var cat = FileCategories.Classify(child.Name);
                byCat[cat] = byCat.GetValueOrDefault(cat) + child.Size;
            }
        }
        Walk(node);

        var cats = byCat.Where(kv => kv.Value > 0)
            .OrderByDescending(kv => kv.Value)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();
        return (files, cats);
    }
}
