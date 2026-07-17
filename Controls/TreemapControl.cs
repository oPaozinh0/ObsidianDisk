using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;
using ObsidianDisk.Models;

namespace ObsidianDisk.Controls;

/// <summary>
/// Expõe o mapa a leitores de tela. O treemap é desenhado à mão, então sem isto
/// ele seria um vazio absoluto para o Narrator/NVDA. O nome reflete o bloco
/// selecionado e é reanunciado a cada movimento das setas.
/// </summary>
public sealed class TreemapAutomationPeer : FrameworkElementAutomationPeer
{
    public TreemapAutomationPeer(TreemapControl owner) : base(owner) { }

    private TreemapControl Map => (TreemapControl)Owner;

    protected override string GetClassNameCore() => nameof(TreemapControl);
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Custom;
    protected override string GetNameCore() => Map.AccessibleDescription();
    protected override string GetHelpTextCore() => L.T("Map.A11yHelp");
    protected override bool IsContentElementCore() => true;
    protected override bool IsControlElementCore() => true;
}

public sealed class TreemapControl : FrameworkElement
{
    private const double HeaderHeight = 18;
    private const double Padding = 3;
    private const double MinVisibleArea = 24; // px² mínimo para desenhar um nó
    private const double CornerRadius = 3;

    private readonly List<DisplayRect> _displayRects = new();
    private FileSystemNode? _root;
    private FileSystemNode? _hovered;
    private FileSystemNode? _selected;

    public event Action<FileSystemNode?>? HoverChanged;
    public event Action<FileSystemNode>? NodeActivated; // duplo clique em pasta
    public event Action<FileSystemNode?>? SelectionChanged;

    private sealed record DisplayRect(FileSystemNode Node, Rect Bounds, int Depth);

    // ---- Tipografia e pincéis (cacheados) ----
    private static readonly FontFamily AppFontFamily =
        (FontFamily)System.Windows.Application.Current.FindResource("AppFont");
    private static readonly Typeface LabelFace = new(AppFontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface SmallFace = new(AppFontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private static Brush DirHeaderText = null!;
    private static readonly Brush FileText = Frozen(new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)));
    private static readonly Pen HoverPen = FrozenPen(Color.FromRgb(0xA8, 0x8B, 0xFF), 2);
    private static readonly Pen SelectedPen = FrozenPen(Color.FromRgb(0xE5, 0xC0, 0x7B), 2);

    private static Brush[] DirFills = null!;
    private static Pen[] DirBorders = null!;
    private static Dictionary<Services.FileCategory, Brush> CategoryFills = null!;
    private static Brush DimOverlay = null!;

    static TreemapControl() => RebuildPalette();

    /// <summary>Recalcula a paleta após troca de tema ou do modo daltônico.</summary>
    public static void RebuildPalette()
    {
        DirHeaderText = Frozen(new SolidColorBrush(
            App.IsLightTheme ? Color.FromRgb(0x3A, 0x43, 0x58) : Color.FromRgb(0xC9, 0xD1, 0xE0)));
        DirFills = CreateDepthFills();
        DirBorders = CreateDepthBorders();
        CategoryFills = Services.FileCategories.All.ToDictionary(
            c => c, c => Frozen(new SolidColorBrush(Services.FileCategories.MapColorOf(c))));
        DimOverlay = Frozen(new SolidColorBrush(App.IsLightTheme
            ? Color.FromArgb(0xB8, 0xF2, 0xF3, 0xF7)
            : Color.FromArgb(0xB8, 0x0E, 0x11, 0x17)));
    }

    public TreemapControl()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        Focusable = true;  // navegável por teclado e alcançável por leitores de tela
    }

    /// <summary>Nó atualmente selecionado (por clique ou teclado).</summary>
    public FileSystemNode? SelectedNode => _selected;

    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() =>
        new TreemapAutomationPeer(this);

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        // Ao receber foco sem seleção, começa pelo maior bloco
        if (_selected is null && _displayRects.Count > 0)
        {
            SelectAndAnnounce(_displayRects[0].Node);
            InvalidateVisual();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_root is null || _displayRects.Count == 0) return;

        switch (e.Key)
        {
            case Key.Left or Key.Right or Key.Up or Key.Down:
                MoveSelection(e.Key);
                e.Handled = true;
                break;

            case Key.Enter or Key.Space:
                if (_selected is { IsDirectory: true } dir)
                {
                    NodeActivated?.Invoke(dir);
                    e.Handled = true;
                }
                break;

            case Key.Home:
                SelectAndAnnounce(_displayRects[0].Node);
                InvalidateVisual();
                e.Handled = true;
                break;

            case Key.Apps:
                if (ContextMenu is not null && _selected is not null)
                {
                    _hovered = _selected;
                    HoverChanged?.Invoke(_selected); // sincroniza o alvo do menu
                    ContextMenu.PlacementTarget = this;
                    ContextMenu.IsOpen = true;
                    e.Handled = true;
                }
                break;
        }
    }

    /// <summary>Move a seleção para o bloco vizinho mais próximo na direção da seta.</summary>
    private void MoveSelection(Key key)
    {
        if (_selected is null)
        {
            SelectAndAnnounce(_displayRects[0].Node);
            InvalidateVisual();
            return;
        }

        if (!TryGetRect(_selected, out var current)) return;
        var from = new Point(current.X + current.Width / 2, current.Y + current.Height / 2);

        FileSystemNode? best = null;
        double bestScore = double.MaxValue;

        foreach (var candidate in _displayRects)
        {
            if (ReferenceEquals(candidate.Node, _selected)) continue;

            var to = new Point(candidate.Bounds.X + candidate.Bounds.Width / 2,
                               candidate.Bounds.Y + candidate.Bounds.Height / 2);
            double dx = to.X - from.X, dy = to.Y - from.Y;

            // só considera quem está na direção certa
            bool ok = key switch
            {
                Key.Left => dx < -1,
                Key.Right => dx > 1,
                Key.Up => dy < -1,
                Key.Down => dy > 1,
                _ => false,
            };
            if (!ok) continue;

            // distância na direção + penalidade pelo desvio lateral
            bool horizontal = key is Key.Left or Key.Right;
            double along = Math.Abs(horizontal ? dx : dy);
            double across = Math.Abs(horizontal ? dy : dx);
            double score = along + across * 2;

            if (score < bestScore) { bestScore = score; best = candidate.Node; }
        }

        if (best is not null)
        {
            SelectAndAnnounce(best);
            InvalidateVisual();
        }
    }

    private void SelectAndAnnounce(FileSystemNode node)
    {
        _selected = node;
        _hovered = node; // status e menu de contexto seguem a seleção
        SelectionChanged?.Invoke(node);
        HoverChanged?.Invoke(node);

        // Faz o leitor de tela ler o bloco recém-selecionado
        if (System.Windows.Automation.Peers.AutomationPeer.ListenerExists(
                System.Windows.Automation.Peers.AutomationEvents.PropertyChanged))
            UIElementAutomationPeer.FromElement(this)?.RaiseAutomationEvent(
                System.Windows.Automation.Peers.AutomationEvents.PropertyChanged);
    }

    /// <summary>Descrição falada do bloco selecionado (ou da visão, se nenhum).</summary>
    internal string AccessibleDescription()
    {
        if (_selected is null || _root is null)
            return L.T("Map.A11yName");

        double percent = _root.Size > 0 ? _selected.Size * 100.0 / _root.Size : 0;
        string kind = _selected.IsDirectory
            ? L.F("Hover.Items", _selected.Children.Count)
            : L.T("Hover.File");
        string text = L.F("Map.A11yBlock", _selected.Name,
            FileSystemNode.FormatSize(_selected.Size), percent.ToString("0.#")) + " · " + kind;

        if (Services.SafetyDatabase.Lookup(_selected) is { } safety)
            text += $". {Services.SafetyDatabase.LabelOf(safety.Level)}: {safety.Description}";

        return text;
    }

    private DrawingGroup? _cache;
    private bool _layoutDirty = true;

    /// <summary>Re-renderiza o mapa por completo (árvore mudou). Hover NÃO passa por aqui.</summary>
    public void Refresh()
    {
        _layoutDirty = true;
        InvalidateVisual();
    }

    public FileSystemNode? Root
    {
        get => _root;
        set
        {
            _root = value;
            _hovered = null;
            _selected = null;
            Refresh();
        }
    }

    private Services.FileCategory? _categoryFilter;
    private bool _highlightOld;
    private DateTime _oldThresholdUtc;

    /// <summary>Se definido, arquivos de outras categorias são atenuados.</summary>
    public Services.FileCategory? CategoryFilter
    {
        get => _categoryFilter;
        set { _categoryFilter = value; Refresh(); }
    }

    /// <summary>Se ativo, arquivos modificados há menos de 1 ano são atenuados (os antigos saltam à vista).</summary>
    public bool HighlightOld
    {
        get => _highlightOld;
        set { _highlightOld = value; Refresh(); }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        _layoutDirty = true;
        base.OnRenderSizeChanged(sizeInfo);
    }


    private bool IsDimmed(FileSystemNode node)
    {
        if (node.IsDirectory) return false;
        if (_categoryFilter is { } filter && Services.FileCategories.Classify(node.Name) != filter)
            return true;
        if (_highlightOld && node.LastWriteUtc > _oldThresholdUtc)
            return true;
        return false;
    }

    private static Brush Frozen(Brush b) { b.Freeze(); return b; }

    private static Pen FrozenPen(Color c, double thickness)
    {
        var p = new Pen(new SolidColorBrush(c), thickness);
        p.Freeze();
        return p;
    }

    private static Brush[] CreateDepthFills()
    {
        // Tons que se acentuam com a profundidade — variante por tema
        var colors = App.IsLightTheme
            ? new[]
            {
                Color.FromRgb(0xEC, 0xEE, 0xF5),
                Color.FromRgb(0xE2, 0xE5, 0xF0),
                Color.FromRgb(0xD8, 0xDC, 0xEB),
                Color.FromRgb(0xCE, 0xD3, 0xE6),
                Color.FromRgb(0xC4, 0xCA, 0xE1),
                Color.FromRgb(0xBA, 0xC1, 0xDC),
            }
            : new[]
            {
                Color.FromRgb(0x1B, 0x1F, 0x2B),
                Color.FromRgb(0x22, 0x27, 0x37),
                Color.FromRgb(0x2A, 0x30, 0x44),
                Color.FromRgb(0x32, 0x39, 0x51),
                Color.FromRgb(0x3A, 0x42, 0x5E),
                Color.FromRgb(0x42, 0x4B, 0x6B),
            };
        return colors.Select(c => Frozen(new SolidColorBrush(c))).ToArray();
    }

    private static Pen[] CreateDepthBorders()
    {
        var colors = App.IsLightTheme
            ? new[]
            {
                Color.FromRgb(0xD5, 0xD9, 0xE4),
                Color.FromRgb(0xCB, 0xD0, 0xDE),
                Color.FromRgb(0xC1, 0xC7, 0xD8),
                Color.FromRgb(0xB7, 0xBE, 0xD2),
                Color.FromRgb(0xAD, 0xB5, 0xCC),
                Color.FromRgb(0xA3, 0xAC, 0xC6),
            }
            : new[]
            {
                Color.FromRgb(0x30, 0x37, 0x4A),
                Color.FromRgb(0x39, 0x41, 0x58),
                Color.FromRgb(0x43, 0x4C, 0x66),
                Color.FromRgb(0x4D, 0x57, 0x74),
                Color.FromRgb(0x57, 0x62, 0x82),
                Color.FromRgb(0x61, 0x6D, 0x90),
            };
        return colors.Select(c => FrozenPen(c, 1)).ToArray();
    }

    private static Brush FillFor(FileSystemNode node, int depth)
    {
        if (node.IsDirectory)
            return DirFills[Math.Min(depth, DirFills.Length - 1)];

        return CategoryFills[Services.FileCategories.Classify(node.Name)];
    }

    // ---------------- Layout + renderização ----------------

    protected override void OnRender(DrawingContext dc)
    {
        // O layout completo (caro) fica cacheado num DrawingGroup; mudanças de hover
        // redesenham apenas o contorno por cima — sem re-layout, sem piscar.
        if (_layoutDirty || _cache is null)
            RebuildCache();

        if (_cache is not null)
            dc.DrawDrawing(_cache);

        DrawHighlights(dc);
    }

    private void RebuildCache()
    {
        _layoutDirty = false;
        _displayRects.Clear();

        var cache = new DrawingGroup();
        using (var dc = cache.Open())
        {
            var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
            dc.DrawRectangle(Brushes.Transparent, null, bounds); // garante hit-testing em toda a área

            if (_root is not null && _root.Size > 0 && bounds.Width >= 10 && bounds.Height >= 10)
            {
                _oldThresholdUtc = DateTime.UtcNow.AddYears(-1);

                // A árvore pode estar sendo preenchida pelo scan em outra thread;
                // se uma inconsistência momentânea aparecer, apenas pula este quadro.
                try
                {
                    LayoutChildren(dc, _root, bounds, depth: 0);
                }
                catch (ArgumentOutOfRangeException) { }
                catch (InvalidOperationException) { }
            }
        }
        cache.Freeze();
        _cache = cache;
    }

    private void LayoutChildren(DrawingContext dc, FileSystemNode dir, Rect area, int depth)
    {
        if (area.Width < 4 || area.Height < 4)
            return;

        // Snapshot indexado: o scanner apenas anexa filhos, então ler até o
        // Count capturado é seguro mesmo durante o scan ao vivo.
        var children = dir.Children;
        int count = children.Count;
        var visible = new List<FileSystemNode>(count);
        for (int i = 0; i < count; i++)
        {
            var child = children[i];
            if (child.Size > 0)
                visible.Add(child);
        }
        if (visible.Count == 0)
            return;

        // Durante o scan os filhos ainda não estão ordenados — o squarified exige ordem decrescente
        visible.Sort(static (a, b) => b.Size.CompareTo(a.Size));

        long total = 0;
        foreach (var v in visible) total += v.Size;
        if (total <= 0) return;

        Squarify(visible, total, area, (node, rect) =>
        {
            if (rect.Width * rect.Height < MinVisibleArea) return;
            DrawNode(dc, node, rect, depth);
        });
    }

    private void DrawNode(DrawingContext dc, FileSystemNode node, Rect rect, int depth)
    {
        var fill = FillFor(node, depth);
        var border = DirBorders[Math.Min(depth, DirBorders.Length - 1)];

        dc.DrawRoundedRectangle(fill, border, Snap(rect), CornerRadius, CornerRadius);
        _displayRects.Add(new DisplayRect(node, rect, depth));

        if (node.IsDirectory)
        {
            bool drawHeader = rect.Height >= HeaderHeight + 14 && rect.Width >= 44;
            if (drawHeader)
                DrawLabel(dc, $"{node.Name}  ·  {FileSystemNode.FormatSize(node.Size)}", rect, isHeader: true);

            var inner = new Rect(
                rect.X + Padding,
                rect.Y + (drawHeader ? HeaderHeight : Padding),
                Math.Max(0, rect.Width - Padding * 2),
                Math.Max(0, rect.Height - (drawHeader ? HeaderHeight : Padding) - Padding));

            if (inner.Width >= 8 && inner.Height >= 8)
                LayoutChildren(dc, node, inner, depth + 1);
        }
        else
        {
            if (rect.Width >= 44 && rect.Height >= 24)
            {
                // Arquivos: rótulo centralizado no bloco, como no SpaceSniffer
                DrawCenteredLabel(dc, node.Name, FileSystemNode.FormatSize(node.Size), rect);
            }

            // Filtro/realce: atenua o que não interessa
            if (IsDimmed(node))
                dc.DrawRoundedRectangle(DimOverlay, null, Snap(rect), CornerRadius, CornerRadius);
        }
    }

    private void DrawLabel(DrawingContext dc, string text, Rect rect, bool isHeader)
    {
        double maxWidth = rect.Width - 10;
        if (maxWidth < 16) return;

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            isHeader ? LabelFace : SmallFace, isHeader ? 11.5 : 11,
            isHeader ? DirHeaderText : FileText, dpi)
        {
            MaxTextWidth = maxWidth,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        dc.DrawText(ft, new Point(rect.X + 5, rect.Y + 2));
    }

    private void DrawCenteredLabel(DrawingContext dc, string name, string size, Rect rect)
    {
        double maxWidth = rect.Width - 8;
        if (maxWidth < 16) return;

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        var nameText = new FormattedText(name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            LabelFace, 11, FileText, dpi)
        {
            MaxTextWidth = maxWidth,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
        };

        bool showSize = rect.Height >= 38;
        FormattedText? sizeText = null;
        if (showSize)
        {
            sizeText = new FormattedText(size, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                SmallFace, 10, FileText, dpi)
            {
                MaxTextWidth = maxWidth,
                MaxLineCount = 1,
                TextAlignment = TextAlignment.Center,
            };
        }

        double totalHeight = nameText.Height + (sizeText?.Height ?? 0);
        double y = rect.Y + Math.Max(1, (rect.Height - totalHeight) / 2);

        dc.DrawText(nameText, new Point(rect.X + 4, y));
        if (sizeText is not null)
            dc.DrawText(sizeText, new Point(rect.X + 4, y + nameText.Height));
    }

    private void DrawHighlights(DrawingContext dc)
    {
        if (_selected is not null && TryGetRect(_selected, out var selRect))
            dc.DrawRoundedRectangle(null, SelectedPen, Snap(selRect), CornerRadius, CornerRadius);

        if (_hovered is not null && !ReferenceEquals(_hovered, _selected) && TryGetRect(_hovered, out var hovRect))
            dc.DrawRoundedRectangle(null, HoverPen, Snap(hovRect), CornerRadius, CornerRadius);
    }

    private bool TryGetRect(FileSystemNode node, out Rect rect)
    {
        for (int i = _displayRects.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_displayRects[i].Node, node))
            {
                rect = _displayRects[i].Bounds;
                return true;
            }
        }
        rect = default;
        return false;
    }

    private static Rect Snap(Rect r) =>
        new(Math.Round(r.X) + 0.5, Math.Round(r.Y) + 0.5, Math.Max(1, Math.Round(r.Width) - 1), Math.Max(1, Math.Round(r.Height) - 1));

    // ---------------- Algoritmo squarified ----------------

    private static void Squarify(List<FileSystemNode> items, long total, Rect area, Action<FileSystemNode, Rect> emit)
    {
        double totalArea = area.Width * area.Height;
        var scaled = items
            .Select(n => (Node: n, Area: n.Size / (double)total * totalArea))
            .Where(x => x.Area >= 1)
            .ToList();

        var rect = area;
        int index = 0;

        while (index < scaled.Count)
        {
            bool horizontal = rect.Width >= rect.Height;
            double side = horizontal ? rect.Height : rect.Width;
            if (side < 1) break;

            // Cresce a linha enquanto a razão de aspecto melhorar
            int end = index + 1;
            double rowArea = scaled[index].Area;
            double worst = WorstRatio(scaled, index, end, rowArea, side);

            while (end < scaled.Count)
            {
                double nextRowArea = rowArea + scaled[end].Area;
                double nextWorst = WorstRatio(scaled, index, end + 1, nextRowArea, side);
                if (nextWorst > worst) break;
                worst = nextWorst;
                rowArea = nextRowArea;
                end++;
            }

            // Emite a linha
            double thickness = rowArea / side;
            double offset = 0;
            for (int i = index; i < end; i++)
            {
                double length = scaled[i].Area / thickness;
                Rect cell = horizontal
                    ? new Rect(rect.X, rect.Y + offset, thickness, length)
                    : new Rect(rect.X + offset, rect.Y, length, thickness);
                emit(scaled[i].Node, cell);
                offset += length;
            }

            rect = horizontal
                ? new Rect(rect.X + thickness, rect.Y, Math.Max(0, rect.Width - thickness), rect.Height)
                : new Rect(rect.X, rect.Y + thickness, rect.Width, Math.Max(0, rect.Height - thickness));

            index = end;
        }
    }

    private static double WorstRatio(List<(FileSystemNode Node, double Area)> items, int start, int end, double rowArea, double side)
    {
        double thickness = rowArea / side;
        if (thickness <= 0) return double.MaxValue;

        double worst = 1;
        for (int i = start; i < end; i++)
        {
            double length = items[i].Area / thickness;
            if (length <= 0) return double.MaxValue;
            double ratio = Math.Max(thickness / length, length / thickness);
            worst = Math.Max(worst, ratio);
        }
        return worst;
    }

    // ---------------- Interação ----------------

    private FileSystemNode? HitTest(Point p)
    {
        for (int i = _displayRects.Count - 1; i >= 0; i--)
            if (_displayRects[i].Bounds.Contains(p))
                return _displayRects[i].Node;
        return null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var hit = HitTest(e.GetPosition(this));
        if (!ReferenceEquals(hit, _hovered))
        {
            _hovered = hit;
            HoverChanged?.Invoke(hit);
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_hovered is not null)
        {
            _hovered = null;
            HoverChanged?.Invoke(null);
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus(); // clicar traz o foco de teclado para o mapa
        var hit = HitTest(e.GetPosition(this));

        if (e.ClickCount == 2 && hit is { IsDirectory: true })
        {
            NodeActivated?.Invoke(hit);
            return;
        }

        if (!ReferenceEquals(hit, _selected))
        {
            _selected = hit;
            SelectionChanged?.Invoke(hit);
            InvalidateVisual();
        }
    }
}
