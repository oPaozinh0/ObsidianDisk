using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

    // Drill-down guiado ("onde foi parar meu espaço?")
    private System.Windows.Threading.DispatcherTimer? _guideTimer;
    private List<FileSystemNode> _guidePath = new();
    private int _guideStep;
    private bool _guiding;

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
        if (!_guiding) _guideTimer?.Stop(); // navegação manual cancela o drill guiado

        _viewRoot = node;
        Treemap.Root = node;
        UpButton.IsEnabled = node.Parent is not null;
        GuideButton.IsEnabled = BuildDominantPath(node).Count > 0;
        EmptyState.Visibility = Visibility.Collapsed;
        BuildBreadcrumb(node);
        if (_listMode) BuildList();
    }

    // ---------------- Drill-down guiado ----------------

    /// <summary>
    /// Do nó atual, desce sempre pela maior subpasta enquanto ela dominar o espaço,
    /// parando onde o espaço se divide entre irmãs ou onde um arquivo é o maior item —
    /// o "culpado" pelo consumo. Anima passo a passo para o usuário ver o caminho.
    /// </summary>
    private void Guide_Click(object sender, RoutedEventArgs e)
    {
        if (_viewRoot is null) return;
        var path = BuildDominantPath(_viewRoot);
        if (path.Count == 0) return;

        if (_listMode) SetListMode(false); // o "zoom" é visível no mosaico

        _guidePath = path;
        _guideStep = 0;
        _guideTimer?.Stop();
        _guideTimer = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromMilliseconds(420) };
        _guideTimer.Tick += GuideStep;
        _guideTimer.Start();
        GuideStep(this, EventArgs.Empty); // primeiro passo imediato
    }

    private void GuideStep(object? sender, EventArgs e)
    {
        if (_guideStep >= _guidePath.Count)
        {
            _guideTimer?.Stop();
            return;
        }
        _guiding = true;
        SetViewRoot(_guidePath[_guideStep++]);
        _guiding = false;
    }

    /// <summary>Cadeia de subpastas dominantes a partir de <paramref name="start"/> (vazia se já é o culpado).</summary>
    private static List<FileSystemNode> BuildDominantPath(FileSystemNode start)
    {
        var path = new List<FileSystemNode>();
        var node = start;

        for (int guard = 0; guard < 24; guard++)
        {
            FileSystemNode? biggest = null, biggestDir = null;
            foreach (var c in node.Children)
            {
                if (c.Size <= 0) continue;
                if (biggest is null || c.Size > biggest.Size) biggest = c;
                if (c.IsDirectory && (biggestDir is null || c.Size > biggestDir.Size)) biggestDir = c;
            }

            if (biggestDir is null) break;                       // sem subpasta pra descer
            if (biggest is { IsDirectory: false }) break;        // um arquivo é o maior item: culpado é a pasta atual
            if (biggestDir.Size < node.Size * 0.5) break;        // espaço se divide entre irmãs: ponto de ramificação

            path.Add(biggestDir);
            node = biggestDir;
        }

        return path;
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

        // Colapsa níveis do meio quando o caminho é longo: primeiro › … › últimos 3
        var display = new List<(FileSystemNode Node, bool Overflow)>();
        if (chain.Count > 5)
        {
            display.Add((chain[0], false));
            display.Add((chain[^4], true)); // "…" salta para o ancestral oculto
            for (int i = chain.Count - 3; i < chain.Count; i++) display.Add((chain[i], false));
        }
        else
        {
            foreach (var n in chain) display.Add((n, false));
        }

        for (int i = 0; i < display.Count; i++)
        {
            var (target, overflow) = display[i];
            bool isLast = i == display.Count - 1;

            if (i > 0) BreadcrumbPanel.Children.Add(MakeChevron());
            BreadcrumbPanel.Children.Add(MakeCrumb(target, isLast, overflow));
        }
    }

    private TextBlock MakeChevron()
    {
        var sep = new TextBlock
        {
            Text = ((char)0xE76C).ToString(), // ChevronRight
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 4, 0),
            Opacity = 0.55,
        };
        sep.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        return sep;
    }

    private Button MakeCrumb(FileSystemNode node, bool isLast, bool overflow)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        // Ícone: reticências (overflow), pasta ou arquivo
        char glyph = overflow ? (char)0xE712 : node.IsDirectory ? (char)0xE8B7 : (char)0xE8A5;
        var icon = new TextBlock
        {
            Text = glyph.ToString(),
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = overflow ? 13 : 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        icon.SetResourceReference(TextBlock.ForegroundProperty, isLast ? "Accent" : "Muted");
        row.Children.Add(icon);

        if (!overflow)
        {
            var label = new TextBlock
            {
                Text = node.Name.TrimEnd('\\'),
                FontSize = 12.5,
                FontWeight = isLast ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, isLast ? "Accent" : "Text");
            row.Children.Add(label);
        }

        var button = new Button { Style = (Style)FindResource("CrumbButton"), Content = row };
        button.Click += (_, _) => SetViewRoot(node);
        return button;
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
        var children = _viewRoot.Children
            .Where(c => c.Size > 0 && Treemap.IsRelevantToSearch(c))
            .OrderByDescending(c => c.Size).ToList();

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

    // ---------------- Busca ----------------

    // A busca recalcula o conjunto de acertos varrendo a árvore inteira; num C: grande
    // fazer isso a cada tecla engasga. Um debounce curto agrupa a digitação num único passe.
    private System.Windows.Threading.DispatcherTimer? _searchTimer;

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (Treemap is null) return; // inicialização

        // Feedback visual imediato (barato); o filtro pesado é adiado
        bool empty = SearchBox.Text.Length == 0;
        SearchPlaceholder.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        SearchClear.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;

        _searchTimer ??= CreateSearchTimer();
        _searchTimer.Stop();
        if (empty) ApplySearch(); // limpar deve ser instantâneo
        else _searchTimer.Start();
    }

    private System.Windows.Threading.DispatcherTimer CreateSearchTimer()
    {
        var timer = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromMilliseconds(180) };
        timer.Tick += (_, _) => ApplySearch();
        return timer;
    }

    private void ApplySearch()
    {
        _searchTimer?.Stop();
        Treemap.SearchQuery = SearchBox.Text;
        if (_listMode) BuildList();
    }

    private void SearchClear_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        SearchBox.Focus();
    }

    /// <summary>Foca o campo de busca (usado pelo atalho Ctrl+F).</summary>
    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    // ---------------- Exportar imagem ----------------

    /// <summary>Salva a visão atual (mosaico ou lista) como PNG, na resolução nativa da tela.</summary>
    private void ExportImage_Click(object sender, RoutedEventArgs e)
    {
        FrameworkElement target = _listMode ? ItemsList : Treemap;
        if (target.ActualWidth < 4 || target.ActualHeight < 4) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = L.T("Map.ExportImageTitle"),
            FileName = $"obsidiandisk-mapa-{DateTime.Now:yyyyMMdd}.png",
            Filter = "PNG (*.png)|*.png",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        var dpi = VisualTreeHelper.GetDpi(target);
        int pw = (int)Math.Ceiling(target.ActualWidth * dpi.DpiScaleX);
        int ph = (int)Math.Ceiling(target.ActualHeight * dpi.DpiScaleY);

        var rtb = new RenderTargetBitmap(pw, ph, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);

        // Fundo opaco (o mosaico é transparente nas bordas): usa a cor do painel do tema
        var background = (FindResource("Panel") as SolidColorBrush)?.Color ?? Colors.Black;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(background), null,
                new Rect(0, 0, target.ActualWidth, target.ActualHeight));
            dc.DrawRectangle(new VisualBrush(target), null,
                new Rect(0, 0, target.ActualWidth, target.ActualHeight));
        }
        rtb.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using (var fs = File.Create(dialog.FileName))
            encoder.Save(fs);

        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
        }
        catch { }
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

        // Posiciona por RenderTransform (não por Margin): mexer na Margin dispara
        // um passe de layout a cada movimento do mouse — era a causa do piscar.
        double tipW = HoverTip.ActualWidth, tipH = HoverTip.ActualHeight;

        double x = p.X + 16, y = p.Y + 18;
        if (x + tipW > host.ActualWidth - 4) x = p.X - tipW - 10;
        if (y + tipH > host.ActualHeight - 4) y = p.Y - tipH - 10;

        TipOffset.X = Math.Max(0, x);
        TipOffset.Y = Math.Max(0, y);
    }

    private void HideTip()
    {
        _tipNode = null;
        HoverTip.Visibility = Visibility.Collapsed;
    }

    // ---------------- Legenda ----------------

    /// <summary>Reconstrói a legenda (cores mudam com tema/daltonismo).</summary>
    public void RebuildLegend()
    {
        LegendPanel.Children.Clear();
        BuildLegend();
    }

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
