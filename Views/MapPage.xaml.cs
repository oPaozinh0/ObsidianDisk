using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ObsidianDisk.Models;
using ObsidianDisk.Services;

namespace ObsidianDisk.Views;

public partial class MapPage : UserControl
{
    private FileSystemNode? _viewRoot;
    private FileSystemNode? _tipNode;

    public event Action<FileSystemNode?>? HoverChanged;

    public Controls.TreemapControl TreemapControl => Treemap;
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
