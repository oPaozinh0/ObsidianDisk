using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ObsidianDisk.Models;

namespace ObsidianDisk.Controls;

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
    private static readonly Typeface LabelFace = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface SmallFace = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private static readonly Brush DirHeaderText = Frozen(new SolidColorBrush(Color.FromRgb(0xC9, 0xD1, 0xE0)));
    private static readonly Brush FileText = Frozen(new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)));
    private static readonly Pen HoverPen = FrozenPen(Color.FromRgb(0xA8, 0x8B, 0xFF), 2);
    private static readonly Pen SelectedPen = FrozenPen(Color.FromRgb(0xE5, 0xC0, 0x7B), 2);

    private static readonly Brush[] DirFills = CreateDepthFills();
    private static readonly Pen[] DirBorders = CreateDepthBorders();
    private static readonly Dictionary<Services.FileCategory, Brush> CategoryFills =
        Services.FileCategories.All.ToDictionary(c => c, c => Frozen(new SolidColorBrush(Services.FileCategories.MapColorOf(c))));

    public TreemapControl()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
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

    private static readonly Brush DimOverlay = Frozen(new SolidColorBrush(Color.FromArgb(0xB8, 0x0E, 0x11, 0x17)));

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
        // Tons de "obsidiana": azul-arroxeados escuros que clareiam com a profundidade
        var colors = new[]
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
        var colors = new[]
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
