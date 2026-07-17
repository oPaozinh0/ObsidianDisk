using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ObsidianDisk.Models;
using ObsidianDisk.Services;

namespace ObsidianDisk.Views;

public sealed class LargeFileEntry : System.ComponentModel.INotifyPropertyChanged
{
    public required FileSystemNode Node { get; init; }
    public required string CategoryLabel { get; init; }
    public required Brush CategoryBrush { get; init; }
    public required string Name { get; init; }
    public required string Directory { get; init; }
    public required string SizeText { get; init; }
    public required long Size { get; init; }

    private bool _isChecked;
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;
            _isChecked = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsChecked)));
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

public partial class LargeFilesPage : UserControl
{
    private static readonly (string Label, long Bytes)[] MinSizes =
    {
        ("≥ 10 MB", 10L << 20),
        ("≥ 50 MB", 50L << 20),
        ("≥ 100 MB", 100L << 20),
        ("≥ 500 MB", 500L << 20),
        ("≥ 1 GB", 1L << 30),
    };

    private FileSystemNode? _root;
    private bool _initializing = true;

    /// <summary>Pedido de exclusão: nós selecionados + se é permanente.</summary>
    public event Action<IReadOnlyList<FileSystemNode>, bool>? DeleteRequested;

    public LargeFilesPage()
    {
        InitializeComponent();
        foreach (var (label, bytes) in MinSizes)
            MinSizeCombo.Items.Add(new ComboBoxItem { Content = label, Tag = bytes });
        MinSizeCombo.SelectedIndex = 2; // 100 MB
        _initializing = false;
    }

    public long MinBytes => (long)(((ComboBoxItem)MinSizeCombo.SelectedItem).Tag ?? 0L);

    public void SetMinBytes(long bytes)
    {
        for (int i = 0; i < MinSizes.Length; i++)
            if (MinSizes[i].Bytes == bytes)
            {
                MinSizeCombo.SelectedIndex = i;
                return;
            }
    }

    public void UpdateFromScan(FileSystemNode? root)
    {
        _root = root;
        Refresh();
    }

    private void MinSize_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing)
            Refresh();
    }

    public void Refresh()
    {
        FilesList.Items.Clear();
        if (_root is null)
        {
            CountText.Text = L.T("Lf.ScanFirst");
            return;
        }

        long min = MinBytes;
        var found = new List<FileSystemNode>();
        Collect(_root);

        void Collect(FileSystemNode dir)
        {
            var children = dir.Children;
            int count = children.Count;
            for (int i = 0; i < count; i++)
            {
                var child = children[i];
                if (child.IsDirectory) Collect(child);
                else if (child.Size >= min) found.Add(child);
            }
        }

        foreach (var node in found.OrderByDescending(n => n.Size).Take(300))
        {
            var cat = FileCategories.Classify(node.Name);
            FilesList.Items.Add(new LargeFileEntry
            {
                Node = node,
                CategoryLabel = FileCategories.LabelOf(cat),
                CategoryBrush = new SolidColorBrush(FileCategories.ColorOf(cat)),
                Name = node.Name,
                Directory = Path.GetDirectoryName(node.FullPath) ?? "",
                SizeText = FileSystemNode.FormatSize(node.Size),
                Size = node.Size,
            });
        }

        CountText.Text = found.Count > 300
            ? L.F("Lf.FoundCapped", found.Count.ToString("N0"))
            : L.F("Lf.Found", found.Count.ToString("N0"));

        SelectAllCheck.IsChecked = false;
        UpdateCheckedCounter();
    }

    private IEnumerable<LargeFileEntry> Entries => FilesList.Items.Cast<LargeFileEntry>();

    /// <summary>Marcados via checkbox; se nenhum, cai para as linhas selecionadas (fluxo avançado).</summary>
    private List<FileSystemNode> TargetNodes()
    {
        var check = Entries.Where(e => e.IsChecked).Select(e => e.Node).ToList();
        return check.Count > 0
            ? check
            : FilesList.SelectedItems.Cast<LargeFileEntry>().Select(e => e.Node).ToList();
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        bool check = SelectAllCheck.IsChecked == true;
        foreach (var entry in Entries)
            entry.IsChecked = check;
        UpdateCheckedCounter();
    }

    private void RowCheck_Click(object sender, RoutedEventArgs e) => UpdateCheckedCounter();

    private void UpdateCheckedCounter()
    {
        var check = Entries.Where(en => en.IsChecked).ToList();
        CheckedText.Text = check.Count > 0
            ? L.F("Lf.Checked", check.Count, FileSystemNode.FormatSize(check.Sum(en => en.Size)))
            : "";
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var node = TargetNodes().FirstOrDefault();
        if (node is null) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{node.FullPath}\"") { UseShellExecute = true });
    }

    private void DeleteRecycle_Click(object sender, RoutedEventArgs e)
    {
        var nodes = TargetNodes();
        if (nodes.Count > 0) DeleteRequested?.Invoke(nodes, false);
    }

    private void DeletePermanent_Click(object sender, RoutedEventArgs e)
    {
        var nodes = TargetNodes();
        if (nodes.Count > 0) DeleteRequested?.Invoke(nodes, true);
    }
}
