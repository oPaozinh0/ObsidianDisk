using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using ObsidianDisk.Models;
using ObsidianDisk.Services;

namespace ObsidianDisk.Views;

/// <summary>
/// "Quero liberar X GB": junta candidatos de várias análises (dev junk, instaladores,
/// pastas/arquivos antigos), remove sobreposições e marca os maiores até bater a meta.
/// Reaproveita a <see cref="DiscoveryEntry"/> e o fluxo de exclusão do restante do app.
/// </summary>
public partial class GoalPage : UserControl
{
    private static readonly (string Label, long Bytes)[] Goals =
    {
        ("5 GB", 5L << 30),
        ("10 GB", 10L << 30),
        ("20 GB", 20L << 30),
        ("50 GB", 50L << 30),
        ("100 GB", 100L << 30),
    };

    private FileSystemNode? _root;

    /// <summary>Pedido de exclusão: nós selecionados + se é permanente (sempre false aqui).</summary>
    public event Action<IReadOnlyList<FileSystemNode>, bool>? DeleteRequested;

    public GoalPage()
    {
        InitializeComponent();
        foreach (var (label, bytes) in Goals)
            GoalCombo.Items.Add(new ComboBoxItem { Content = label, Tag = bytes });
        GoalCombo.SelectedIndex = 1; // 10 GB
    }

    private long CurrentGoalBytes =>
        (GoalCombo.SelectedItem as ComboBoxItem)?.Tag as long? ?? (10L << 30);

    public void UpdateFromScan(FileSystemNode? root)
    {
        _root = root;
        ResultsList.Items.Clear();
        ProgressCard.Visibility = Visibility.Collapsed;
        ActionPanel.IsEnabled = false;
        CountText.Text = root is null ? L.T("Dc.ScanFirst") : "";
    }

    private void Compute_Click(object sender, RoutedEventArgs e)
    {
        ResultsList.Items.Clear();
        SelectAllCheck.IsChecked = false;

        if (_root is null)
        {
            CountText.Text = L.T("Dc.ScanFirst");
            return;
        }

        var candidates = GatherCandidates(_root);

        long goal = CurrentGoalBytes;
        long running = 0;
        long available = candidates.Sum(c => c.Item.Size);

        foreach (var c in candidates)
        {
            bool pick = running < goal;
            if (pick) running += c.Item.Size;
            ResultsList.Items.Add(new DiscoveryEntry
            {
                Node = c.Item.Node,
                Name = c.Item.Name,
                Detail = c.Item.Detail,
                WhenText = c.Reason,
                SizeText = FileSystemNode.FormatSize(c.Item.Size),
                Size = c.Item.Size,
                IsChecked = pick,
            });
        }

        ActionPanel.IsEnabled = candidates.Count > 0;
        ProgressCard.Visibility = candidates.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        CountText.Text = candidates.Count == 0
            ? L.T("Goal.Nothing")
            : L.F("Goal.Found", candidates.Count.ToString("N0"), FileSystemNode.FormatSize(available));

        UpdateProgress();
    }

    private sealed record Candidate(DiscoveryItem Item, string Reason);

    /// <summary>Reúne candidatos de várias análises, tira sobreposições e ordena por tamanho.</summary>
    private static List<Candidate> GatherCandidates(FileSystemNode root)
    {
        var sources = new (string Reason, List<DiscoveryItem> Items)[]
        {
            (L.T("Goal.ReasonDevJunk"), DiscoveryAnalyzer.DevJunk(root)),
            (L.T("Goal.ReasonInstaller"), DiscoveryAnalyzer.Installers(root)),
            (L.T("Goal.ReasonOldFolder"), DiscoveryAnalyzer.GhostFolders(root, DateTime.UtcNow.AddDays(-180), 50L << 20)),
            (L.T("Goal.ReasonOldFile"), DiscoveryAnalyzer.LargeFilesByAge(root, DateTime.UtcNow.AddDays(-365), 100L << 20)),
        };

        var all = new List<Candidate>();
        foreach (var (reason, items) in sources)
            foreach (var item in items)
            {
                if (item.Node is null || item.Size <= 0) continue;
                if (SafetyDatabase.Lookup(item.Node)?.Level == SafetyLevel.Never) continue; // nunca sugere apagar
                all.Add(new Candidate(item, reason));
            }

        // Maior primeiro: assim um ancestral entra antes e seus descendentes são descartados
        all.Sort((a, b) => b.Item.Size.CompareTo(a.Item.Size));

        var accepted = new List<Candidate>();
        foreach (var c in all)
        {
            string path = c.Item.Node!.FullPath;
            if (accepted.Any(a => IsSameOrNested(a.Item.Node!.FullPath, path))) continue;
            accepted.Add(c);
        }
        return accepted;
    }

    /// <summary>True se um caminho é o outro ou está aninhado nele (evita contar espaço duas vezes).</summary>
    private static bool IsSameOrNested(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        string aa = a.TrimEnd('\\') + "\\";
        string bb = b.TrimEnd('\\') + "\\";
        return bb.StartsWith(aa, StringComparison.OrdinalIgnoreCase)
            || aa.StartsWith(bb, StringComparison.OrdinalIgnoreCase);
    }

    private IEnumerable<DiscoveryEntry> Entries => ResultsList.Items.Cast<DiscoveryEntry>();

    private void UpdateProgress()
    {
        long checkedSum = Entries.Where(e => e.IsChecked).Sum(e => e.Size);
        long goal = CurrentGoalBytes;

        GoalBar.Value = goal > 0 ? Math.Min(100, checkedSum * 100.0 / goal) : 0;
        ProgressLabel.Text = L.F("Goal.Progress",
            FileSystemNode.FormatSize(checkedSum), FileSystemNode.FormatSize(goal));
        ProgressHint.Text = checkedSum >= goal
            ? L.T("Goal.Reached")
            : L.F("Goal.Remaining", FileSystemNode.FormatSize(goal - checkedSum));

        int count = Entries.Count(e => e.IsChecked);
        CheckedText.Text = count > 0 ? L.F("Lf.Checked", count, FileSystemNode.FormatSize(checkedSum)) : "";
    }

    private List<FileSystemNode> CheckedNodes() =>
        Entries.Where(e => e.IsChecked && e.Node is not null).Select(e => e.Node!).ToList();

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        bool check = SelectAllCheck.IsChecked == true;
        foreach (var entry in Entries)
            entry.IsChecked = check;
        UpdateProgress();
    }

    private void RowCheck_Click(object sender, RoutedEventArgs e) => UpdateProgress();

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var node = CheckedNodes().FirstOrDefault()
                   ?? (ResultsList.SelectedItem as DiscoveryEntry)?.Node;
        if (node is null) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{node.FullPath}\"") { UseShellExecute = true });
    }

    private void Recycle_Click(object sender, RoutedEventArgs e)
    {
        var nodes = CheckedNodes();
        if (nodes.Count > 0) DeleteRequested?.Invoke(nodes, false);
    }
}
