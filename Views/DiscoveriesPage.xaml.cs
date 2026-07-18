using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using ObsidianDisk.Models;
using ObsidianDisk.Services;

namespace ObsidianDisk.Views;

public sealed class DiscoveryEntry : System.ComponentModel.INotifyPropertyChanged
{
    public FileSystemNode? Node { get; init; }
    public required string Name { get; init; }
    public required string Detail { get; init; }
    public required string WhenText { get; init; }
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

public partial class DiscoveriesPage : UserControl
{
    private enum Mode { Ghost, Extensions, DevJunk, OldFiles, Empty, Installers }

    private const long GhostMinBytes = 50L << 20;    // 50 MB
    private const long OldFileMinBytes = 100L << 20; // 100 MB
    private const int OldDays = 365;                 // "há muito tempo" = 1 ano

    private FileSystemNode? _root;
    private bool _initializing = true;

    /// <summary>Pedido de exclusão: nós selecionados + se é permanente.</summary>
    public event Action<IReadOnlyList<FileSystemNode>, bool>? DeleteRequested;

    public DiscoveriesPage()
    {
        InitializeComponent();
        ModeCombo.Items.Add(new ComboBoxItem { Content = L.T("Dc.ModeGhost"), Tag = Mode.Ghost });
        ModeCombo.Items.Add(new ComboBoxItem { Content = L.T("Dc.ModeExtensions"), Tag = Mode.Extensions });
        ModeCombo.Items.Add(new ComboBoxItem { Content = L.T("Dc.ModeDevJunk"), Tag = Mode.DevJunk });
        ModeCombo.Items.Add(new ComboBoxItem { Content = L.T("Dc.ModeOldFiles"), Tag = Mode.OldFiles });
        ModeCombo.Items.Add(new ComboBoxItem { Content = L.T("Dc.ModeEmpty"), Tag = Mode.Empty });
        ModeCombo.Items.Add(new ComboBoxItem { Content = L.T("Dc.ModeInstallers"), Tag = Mode.Installers });
        ModeCombo.SelectedIndex = 0;
        _initializing = false;
    }

    private Mode CurrentMode => (Mode)(((ComboBoxItem)ModeCombo.SelectedItem).Tag ?? Mode.Ghost);

    public void UpdateFromScan(FileSystemNode? root)
    {
        _root = root;
        Refresh();
    }

    private void Mode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing) Refresh();
    }

    public void Refresh()
    {
        ResultsList.Items.Clear();

        if (_root is null)
        {
            DescText.Text = "";
            CountText.Text = L.T("Dc.ScanFirst");
            return;
        }

        var mode = CurrentMode;
        DescText.Text = mode switch
        {
            Mode.Ghost => L.T("Dc.DescGhost"),
            Mode.Extensions => L.T("Dc.DescExtensions"),
            Mode.DevJunk => L.T("Dc.DescDevJunk"),
            Mode.OldFiles => L.T("Dc.DescOldFiles"),
            Mode.Installers => L.T("Dc.DescInstallers"),
            _ => L.T("Dc.DescEmpty"),
        };

        // Extensões são agregados, não têm nós únicos para abrir/deletar
        ActionPanel.IsEnabled = mode != Mode.Extensions;

        var cutoff = DateTime.UtcNow.AddDays(-OldDays);
        var items = mode switch
        {
            Mode.Ghost => DiscoveryAnalyzer.GhostFolders(_root, cutoff, GhostMinBytes),
            Mode.Extensions => DiscoveryAnalyzer.WastedExtensions(_root),
            Mode.DevJunk => DiscoveryAnalyzer.DevJunk(_root),
            Mode.OldFiles => DiscoveryAnalyzer.LargeFilesByAge(_root, cutoff, OldFileMinBytes),
            Mode.Installers => DiscoveryAnalyzer.Installers(_root),
            _ => DiscoveryAnalyzer.EmptyFolders(_root),
        };

        foreach (var item in items)
            ResultsList.Items.Add(new DiscoveryEntry
            {
                Node = item.Node,
                Name = item.Name,
                Detail = item.Detail,
                WhenText = item.WhenUtc == default ? "" : item.WhenUtc.ToLocalTime().ToString("dd/MM/yyyy"),
                SizeText = FileSystemNode.FormatSize(item.Size),
                Size = item.Size,
            });

        long total = items.Sum(i => i.Size);
        CountText.Text = items.Count == 0
            ? L.T("Dc.Empty")
            : L.F("Dc.Found", items.Count.ToString("N0"), FileSystemNode.FormatSize(total));

        SelectAllCheck.IsChecked = false;
        UpdateCheckedCounter();
    }

    private IEnumerable<DiscoveryEntry> Entries => ResultsList.Items.Cast<DiscoveryEntry>();

    /// <summary>Marcados via checkbox; se nenhum, cai para as linhas selecionadas. Só itens deletáveis.</summary>
    private List<FileSystemNode> TargetNodes()
    {
        var check = Entries.Where(e => e.IsChecked && e.Node is not null).Select(e => e.Node!).ToList();
        if (check.Count > 0) return check;
        return ResultsList.SelectedItems.Cast<DiscoveryEntry>()
            .Where(e => e.Node is not null).Select(e => e.Node!).ToList();
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
