using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ObsidianDisk.Models;
using ObsidianDisk.Services;

namespace ObsidianDisk.Views;

public partial class DuplicatesPage : UserControl
{
    private static readonly (string Label, long Bytes)[] MinSizes =
    {
        ("≥ 1 MB", 1L << 20),
        ("≥ 10 MB", 10L << 20),
        ("≥ 100 MB", 100L << 20),
    };

    private FileSystemNode? _root;
    private CancellationTokenSource? _cts;
    private readonly List<(CheckBox Check, FileSystemNode Node)> _rows = new();

    public event Action<IReadOnlyList<FileSystemNode>, bool>? DeleteRequested;

    public DuplicatesPage()
    {
        InitializeComponent();
        foreach (var (label, bytes) in MinSizes)
            MinSizeCombo.Items.Add(new ComboBoxItem { Content = label, Tag = bytes });
        MinSizeCombo.SelectedIndex = 1; // 10 MB
    }

    public void UpdateFromScan(FileSystemNode? root)
    {
        _root = root;
        // Após novo scan ou exclusão, remove linhas de nós que saíram da árvore
        PruneDeleted();
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        if (_cts is not null)
        {
            _cts.Cancel();
            return;
        }

        if (_root is null)
        {
            SummaryText.Text = L.T("Dup.ScanFirst");
            return;
        }

        long minSize = (long)(((ComboBoxItem)MinSizeCombo.SelectedItem).Tag ?? (10L << 20));
        _cts = new CancellationTokenSource();
        SearchButton.Content = L.T("Dup.CancelSearch");
        ProgressRow.Visibility = Visibility.Visible;
        SearchProgress.IsIndeterminate = true;
        GroupsPanel.Children.Clear();
        _rows.Clear();

        var progress = new Progress<DuplicateProgress>(p =>
        {
            SearchProgress.IsIndeterminate = false;
            SearchProgress.Value = p.Total > 0 ? p.Done * 100.0 / p.Total : 0;
            ProgressLabel.Text = L.F("Dup.PhaseProgress", L.T(p.Phase), p.Done.ToString("N0"), p.Total.ToString("N0"));
        });

        try
        {
            var groups = await DuplicateFinder.FindAsync(_root, minSize, progress, _cts.Token);
            BuildGroups(groups);

            long wasted = groups.Sum(g => g.WastedBytes);
            SummaryText.Text = groups.Count == 0
                ? L.T("Dup.None")
                : L.F("Dup.Summary", groups.Count.ToString("N0"), FileSystemNode.FormatSize(wasted));
        }
        catch (OperationCanceledException)
        {
            SummaryText.Text = L.T("Dup.Cancelled");
        }
        finally
        {
            ProgressRow.Visibility = Visibility.Collapsed;
            SearchButton.Content = L.T("Dup.Search");
            _cts.Dispose();
            _cts = null;
        }
    }

    private void BuildGroups(List<DuplicateGroup> groups)
    {
        GroupsPanel.Children.Clear();
        _rows.Clear();

        foreach (var group in groups.Take(100))
        {
            var card = new Border
            {
                Style = (Style)FindResource("Card"),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 10),
            };
            var stack = new StackPanel();

            stack.Children.Add(new TextBlock
            {
                Text = L.F("Dup.GroupHeader", group.Files.Count, FileSystemNode.FormatSize(group.FileSize), FileSystemNode.FormatSize(group.WastedBytes)),
                Foreground = (Brush)FindResource("Text"),
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 8),
            });

            // Mantém desmarcada a cópia mais recente (é a que fica)
            var newest = group.Files.OrderByDescending(f => f.LastWriteUtc).First();

            foreach (var file in group.Files)
            {
                var check = new CheckBox
                {
                    Style = (Style)FindResource("DarkCheck"),
                    IsChecked = !ReferenceEquals(file, newest),
                    Margin = new Thickness(0, 3, 0, 3),
                };

                var label = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
                label.Inlines.Add(new System.Windows.Documents.Run(file.FullPath)
                {
                    Foreground = (Brush)FindResource("Text"),
                    FontSize = 12.5,
                });
                label.Inlines.Add(new System.Windows.Documents.Run(
                    $"   · {file.LastWriteUtc.ToLocalTime():dd/MM/yyyy}" +
                    (ReferenceEquals(file, newest) ? L.T("Dup.Newest") : ""))
                {
                    Foreground = (Brush)FindResource("Muted"),
                    FontSize = 11.5,
                });
                check.Content = label;

                _rows.Add((check, file));
                stack.Children.Add(check);
            }

            card.Child = stack;
            GroupsPanel.Children.Add(card);
        }

        if (groups.Count > 100)
        {
            GroupsPanel.Children.Add(new TextBlock
            {
                Text = L.F("Dup.ShowingTop", groups.Count.ToString("N0")),
                Foreground = (Brush)FindResource("Muted"),
                FontSize = 12,
                Margin = new Thickness(4, 2, 0, 8),
            });
        }
    }

    private List<FileSystemNode> CheckedNodes() =>
        _rows.Where(r => r.Check.IsChecked == true).Select(r => r.Node).ToList();

    private void DeleteRecycle_Click(object sender, RoutedEventArgs e)
    {
        var nodes = CheckedNodes();
        if (nodes.Count > 0) DeleteRequested?.Invoke(nodes, false);
    }

    private void DeletePermanent_Click(object sender, RoutedEventArgs e)
    {
        var nodes = CheckedNodes();
        if (nodes.Count > 0) DeleteRequested?.Invoke(nodes, true);
    }

    /// <summary>Remove das listas os nós já excluídos (desligados da árvore).</summary>
    public void PruneDeleted()
    {
        var dead = _rows.Where(r => r.Node.Parent is null).ToList();
        if (dead.Count == 0) return;

        foreach (var (check, _) in dead)
        {
            if (check.Parent is StackPanel panel)
                panel.Children.Remove(check);
            _rows.RemoveAll(r => ReferenceEquals(r.Check, check));
        }

        // Remove cards que ficaram com 1 cópia ou menos
        for (int i = GroupsPanel.Children.Count - 1; i >= 0; i--)
        {
            if (GroupsPanel.Children[i] is Border { Child: StackPanel s } &&
                s.Children.OfType<CheckBox>().Count() <= 1)
                GroupsPanel.Children.RemoveAt(i);
        }
    }
}
