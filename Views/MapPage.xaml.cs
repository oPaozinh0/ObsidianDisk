using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ObsidianDisk.Models;
using ObsidianDisk.Services;

namespace ObsidianDisk.Views;

/// <summary>Linha do modo lista — a mesma árvore, comparada por comprimento de barra.</summary>
public sealed record MapRow(
    FileSystemNode Node, string Name, Brush CategoryBrush, string SizeText, string ShareText,
    double BarWidth, string SafetyText, Brush SafetyBrush, Visibility SafetyVisibility, string? SafetyTooltip);

public partial class MapPage : UserControl
{
    private FileSystemNode? _viewRoot;
    private FileSystemNode? _tipNode;
    private bool _listMode;

    public event Action<FileSystemNode?>? HoverChanged;

    public Controls.TreemapControl TreemapControl => Treemap;
    public ListView List => ItemsList;
    public FileSystemNode? ViewRoot => _viewRoot;

    public MapPage()
    {
        InitializeComponent();
        BuildLegend();
        BuildCategoryCombo();

        Treemap.HoverChanged += OnHover;
        Treemap.NodeActivated += SetViewRoot;
        Treemap.MouseMove += Treemap_MouseMove;
        Treemap.MouseLeave += (_, _) => HideTip();
    }

    // ---------------- Navegação ----------------

    public void SetViewRoot(FileSystemNode node)
    {
        _viewRoot = node;
        Treemap.Root = node;
        UpButton.IsEnabled = node.Parent is not null;
        EmptyState.Visibility = Visibility.Collapsed;
        BuildBreadcrumb(node);
        if (_listMode) BuildList();
    }

    /// <summary>Redesenha a visão atual (mosaico ou lista) após mudanças na árvore.</summary>
    public void Refresh()
    {
        if (_listMode) BuildList();
        else Treemap.Refresh();
    }

    public bool GoUp()
    {
        if (_viewRoot?.Parent is { } parent)
        {
            SetViewRoot(parent);
            return true;
        }
        return false;
    }

    private void Up_Click(object sender, RoutedEventArgs e) => GoUp();

    private void BuildBreadcrumb(FileSystemNode node)
    {
        BreadcrumbPanel.Children.Clear();

        var chain = new List<FileSystemNode>();
        for (var n = node; n is not null; n = n.Parent)
            chain.Add(n);
        chain.Reverse();

        for (int i = 0; i < chain.Count; i++)
        {
            var target = chain[i];
            var button = new Button
            {
                Style = (Style)FindResource("CrumbButton"),
                Content = target.Name.TrimEnd('\\'),
            };
            if (i == chain.Count - 1)
                button.Foreground = (Brush)FindResource("Text");
            button.Click += (_, _) => SetViewRoot(target);
            BreadcrumbPanel.Children.Add(button);

            if (i < chain.Count - 1)
            {
                BreadcrumbPanel.Children.Add(new TextBlock
                {
                    Text = "›",
                    Foreground = (Brush)FindResource("Muted"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(2, 0, 2, 0),
                });
            }
        }
    }

    // ---------------- Alternância mosaico / lista ----------------

    private void ViewTreemap_Click(object sender, RoutedEventArgs e) => SetListMode(false);
    private void ViewList_Click(object sender, RoutedEventArgs e) => SetListMode(true);

    private void SetListMode(bool list)
    {
        _listMode = list;
        TreemapViewButton.Variant = list ? Controls.ObsidianButtonVariant.Ghost : Controls.ObsidianButtonVariant.Primary;
        ListViewButton.Variant = list ? Controls.ObsidianButtonVariant.Primary : Controls.ObsidianButtonVariant.Ghost;

        Treemap.Visibility = list ? Visibility.Collapsed : Visibility.Visible;
        ItemsList.Visibility = list ? Visibility.Visible : Visibility.Collapsed;
        if (list)
        {
            HideTip();
            BuildList();
        }
    }

    private void BuildList()
    {
        ItemsList.Items.Clear();
        if (_viewRoot is null) return;

        long total = Math.Max(1, _viewRoot.Size);
        var children = _viewRoot.Children.Where(c => c.Size > 0).OrderByDescending(c => c.Size).ToList();

        foreach (var node in children)
        {
            double share = node.Size * 100.0 / total;
            var color = node.IsDirectory
                ? (Color)FindResource("AccentColor")
                : FileCategories.ColorOf(FileCategories.Classify(node.Name));

            var safety = SafetyDatabase.Lookup(node);
            ItemsList.Items.Add(new MapRow(
                Node: node,
                Name: node.IsDirectory ? node.Name + "\\" : node.Name,
                CategoryBrush: new SolidColorBrush(color),
                SizeText: FileSystemNode.FormatSize(node.Size),
                ShareText: $"{share:0.#}%",
                BarWidth: Math.Max(2, share / 100.0 * 220),
                SafetyText: safety is null ? "" : $"{SafetyDatabase.LabelOf(safety.Level)} · {safety.Description}",
                SafetyBrush: new SolidColorBrush(SafetyDatabase.ColorOf(safety?.Level ?? SafetyLevel.Unknown)),
                SafetyVisibility: safety is null ? Visibility.Collapsed : Visibility.Visible,
                SafetyTooltip: safety is null ? null
                    : safety.Description + (safety.Advice is null ? "" : "\n" + safety.Advice)));
        }
    }

    private void List_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsList.SelectedItem is MapRow { Node.IsDirectory: true } row)
            SetViewRoot(row.Node);
    }

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Alimenta o mesmo status/menu de contexto do mosaico
        HoverChanged?.Invoke((ItemsList.SelectedItem as MapRow)?.Node);
    }

    // ---------------- Filtros ----------------

    private void BuildCategoryCombo()
    {
        CategoryCombo.Items.Add(new ComboBoxItem { Content = L.T("Map.AllCategories"), Tag = null });
        foreach (var cat in FileCategories.All)
            CategoryCombo.Items.Add(new ComboBoxItem { Content = FileCategories.LabelOf(cat), Tag = cat });
        CategoryCombo.SelectedIndex = 0;
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (Treemap is null) return; // inicialização

        Treemap.CategoryFilter = (CategoryCombo.SelectedItem as ComboBoxItem)?.Tag as FileCategory?;
        Treemap.HighlightOld = OldFilesSwitch.IsChecked == true;
    }

    // ---------------- Tooltip flutuante ----------------

    private void OnHover(FileSystemNode? node)
    {
        HoverChanged?.Invoke(node);
        _tipNode = node;

        if (node is null)
        {
            HideTip();
            return;
        }

        TipName.Text = node.Name;
        if (node.IsDirectory)
        {
            TipDetails.Text = L.F("Map.TipFolder", FileSystemNode.FormatSize(node.Size), node.Children.Count);
            TipDate.Visibility = Visibility.Collapsed;
        }
        else
        {
            var cat = FileCategories.Classify(node.Name);
            TipDetails.Text = L.F("Map.TipFile", FileCategories.LabelOf(cat), FileSystemNode.FormatSize(node.Size));
            TipDate.Text = L.F("Map.TipModified", node.LastWriteUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"));
            TipDate.Visibility = Visibility.Visible;
        }

        // O que é isso e dá para apagar?
        var safety = SafetyDatabase.Lookup(node);
        if (safety is null)
        {
            TipSafetyRow.Visibility = Visibility.Collapsed;
        }
        else
        {
            var color = SafetyDatabase.ColorOf(safety.Level);
            TipSafetyDot.Background = new SolidColorBrush(color);
            TipSafetyLevel.Text = SafetyDatabase.LabelOf(safety.Level);
            TipSafetyLevel.Foreground = new SolidColorBrush(color);
            TipSafetyText.Text = safety.Description + (safety.Advice is null ? "" : "\n" + safety.Advice);
            TipSafetyRow.Visibility = Visibility.Visible;
        }

        HoverTip.Visibility = Visibility.Visible;
    }

    private void Treemap_MouseMove(object sender, MouseEventArgs e)
    {
        if (_tipNode is null || HoverTip.Visibility != Visibility.Visible) return;

        var host = (Grid)HoverTip.Parent;
        var p = e.GetPosition(host);

        HoverTip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double tipW = HoverTip.DesiredSize.Width, tipH = HoverTip.DesiredSize.Height;

        double x = p.X + 16, y = p.Y + 18;
        if (x + tipW > host.ActualWidth - 4) x = p.X - tipW - 10;
        if (y + tipH > host.ActualHeight - 4) y = p.Y - tipH - 10;

        HoverTip.Margin = new Thickness(Math.Max(0, x), Math.Max(0, y), 0, 0);
    }

    private void HideTip()
    {
        _tipNode = null;
        HoverTip.Visibility = Visibility.Collapsed;
    }

    // ---------------- Legenda ----------------

    private void BuildLegend()
    {
        foreach (var cat in FileCategories.All)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0) };
            item.Children.Add(new Rectangle
            {
                Width = 10, Height = 10, RadiusX = 2, RadiusY = 2,
                Fill = new SolidColorBrush(FileCategories.ColorOf(cat)),
                VerticalAlignment = VerticalAlignment.Center,
            });
            item.Children.Add(new TextBlock
            {
                Text = FileCategories.LabelOf(cat),
                Foreground = (Brush)FindResource("Muted"),
                FontSize = 11,
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            LegendPanel.Children.Add(item);
        }
    }
}
