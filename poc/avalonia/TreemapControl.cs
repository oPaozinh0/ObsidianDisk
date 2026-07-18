using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ObsidianDisk.Models;

namespace ObsidianDisk.Poc;

/// <summary>
/// Porte do treemap para Avalonia (render via Skia). Mesmo algoritmo squarified e os mesmos
/// rótulos/glifos do app WPF — é justamente o desenho de glifos que artefata na Radeon com o
/// compositor do WPF. Aqui o objetivo é ver se com Skia sai limpo e rápido.
/// </summary>
public sealed class TreemapControl : Control
{
    private const double HeaderHeight = 18;
    private const double Padding = 3;
    private const double MinVisibleArea = 24;
    private const double CornerRadius = 3;

    private readonly List<(FileSystemNode Node, Rect Bounds)> _rects = new();
    private FileSystemNode? _root;
    private FileSystemNode? _hovered;

    // Tons de diretório por profundidade (tema escuro), como no app
    private static readonly Color[] DirFills =
    {
        Color.FromRgb(0x1B, 0x1F, 0x2B), Color.FromRgb(0x22, 0x27, 0x37),
        Color.FromRgb(0x2A, 0x30, 0x44), Color.FromRgb(0x32, 0x39, 0x51),
        Color.FromRgb(0x3A, 0x42, 0x5E), Color.FromRgb(0x42, 0x4B, 0x6B),
    };
    private static readonly Color[] DirBorders =
    {
        Color.FromRgb(0x30, 0x37, 0x4A), Color.FromRgb(0x39, 0x41, 0x58),
        Color.FromRgb(0x43, 0x4C, 0x66), Color.FromRgb(0x4D, 0x57, 0x74),
        Color.FromRgb(0x57, 0x62, 0x82), Color.FromRgb(0x61, 0x6D, 0x90),
    };

    private static readonly IBrush FileText = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF));
    private static readonly IBrush DirHeaderText = new SolidColorBrush(Color.FromRgb(0xC9, 0xD1, 0xE0));
    private static readonly IPen HoverPen = new Pen(new SolidColorBrush(Color.FromRgb(0xA8, 0x8B, 0xFF)), 2);
    private static readonly Typeface LabelFace = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
    private static readonly Typeface SmallFace = new(FontFamily.Default, FontStyle.Normal, FontWeight.Normal);

    public TreemapControl()
    {
        ClipToBounds = true;
    }

    public FileSystemNode? Root
    {
        get => _root;
        set { _root = value; _hovered = null; InvalidateVisual(); }
    }

    // ---------------- Render ----------------

    public override void Render(DrawingContext context)
    {
        _rects.Clear();
        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x0E, 0x11, 0x17)), null, bounds);

        if (_root is null || _root.Size <= 0 || bounds.Width < 10 || bounds.Height < 10) return;

        try { LayoutChildren(context, _root, bounds, 0); }
        catch (ArgumentOutOfRangeException) { }
        catch (InvalidOperationException) { }

        if (_hovered is not null && TryGetRect(_hovered, out var hov))
            context.DrawRectangle(null, HoverPen, hov, CornerRadius, CornerRadius);
    }

    private void LayoutChildren(DrawingContext dc, FileSystemNode dir, Rect area, int depth)
    {
        if (area.Width < 4 || area.Height < 4) return;

        var children = dir.Children;
        var visible = new List<FileSystemNode>(children.Count);
        for (int i = 0; i < children.Count; i++)
            if (children[i].Size > 0) visible.Add(children[i]);
        if (visible.Count == 0) return;

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
        var fill = new SolidColorBrush(node.IsDirectory
            ? DirFills[Math.Min(depth, DirFills.Length - 1)]
            : CategoryColor(node.Name));
        var pen = new Pen(new SolidColorBrush(DirBorders[Math.Min(depth, DirBorders.Length - 1)]), 1);

        dc.DrawRectangle(fill, pen, rect, CornerRadius, CornerRadius);
        _rects.Add((node, rect));

        if (node.IsDirectory)
        {
            bool header = rect.Height >= HeaderHeight + 14 && rect.Width >= 44;
            if (header)
                DrawLabel(dc, $"{node.Name}  ·  {FileSystemNode.FormatSize(node.Size)}", rect, true);

            var inner = new Rect(
                rect.X + Padding,
                rect.Y + (header ? HeaderHeight : Padding),
                Math.Max(0, rect.Width - Padding * 2),
                Math.Max(0, rect.Height - (header ? HeaderHeight : Padding) - Padding));

            if (inner.Width >= 8 && inner.Height >= 8)
                LayoutChildren(dc, node, inner, depth + 1);
        }
        else if (rect.Width >= 44 && rect.Height >= 24)
        {
            DrawCenteredLabel(dc, node.Name, FileSystemNode.FormatSize(node.Size), rect);
        }
    }

    private void DrawLabel(DrawingContext dc, string text, Rect rect, bool isHeader)
    {
        double maxWidth = rect.Width - 10;
        if (maxWidth < 16) return;

        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            isHeader ? LabelFace : SmallFace, isHeader ? 11.5 : 11, isHeader ? DirHeaderText : FileText)
        {
            MaxTextWidth = maxWidth,
            MaxTextHeight = 20,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        dc.DrawText(ft, new Point(rect.X + 5, rect.Y + 2));
    }

    private void DrawCenteredLabel(DrawingContext dc, string name, string size, Rect rect)
    {
        double maxWidth = rect.Width - 8;
        if (maxWidth < 16) return;

        var nameText = new FormattedText(name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            LabelFace, 11, FileText)
        {
            MaxTextWidth = maxWidth,
            MaxTextHeight = 16,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
        };

        bool showSize = rect.Height >= 38;
        FormattedText? sizeText = showSize
            ? new FormattedText(size, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, SmallFace, 10, FileText)
            {
                MaxTextWidth = maxWidth,
                MaxTextHeight = 14,
                TextAlignment = TextAlignment.Center,
            }
            : null;

        double totalHeight = nameText.Height + (sizeText?.Height ?? 0);
        double y = rect.Y + Math.Max(1, (rect.Height - totalHeight) / 2);

        dc.DrawText(nameText, new Point(rect.X + 4, y));
        if (sizeText is not null)
            dc.DrawText(sizeText, new Point(rect.X + 4, y + nameText.Height));
    }

    // ---------------- Cores por categoria (simplificado para o PoC) ----------------

    private static Color CategoryColor(string name)
    {
        var ext = System.IO.Path.GetExtension(name).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".heic" => Color.FromRgb(0x5A, 0xB0, 0xF2),
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" => Color.FromRgb(0xE0, 0x6C, 0x75),
            ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" => Color.FromRgb(0x9C, 0x6C, 0xE0),
            ".zip" or ".rar" or ".7z" or ".gz" or ".iso" => Color.FromRgb(0xE5, 0xC0, 0x7B),
            ".exe" or ".msi" or ".dll" => Color.FromRgb(0x8A, 0x92, 0xA6),
            ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".txt" => Color.FromRgb(0x5F, 0xD3, 0x8A),
            ".cs" or ".js" or ".ts" or ".py" or ".json" or ".xml" or ".html" => Color.FromRgb(0xC6, 0x8A, 0xF2),
            _ => Color.FromRgb(0x6B, 0x72, 0x86),
        };
    }

    // ---------------- Interação (hover) ----------------

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        FileSystemNode? hit = null;
        for (int i = _rects.Count - 1; i >= 0; i--)
            if (_rects[i].Bounds.Contains(p)) { hit = _rects[i].Node; break; }

        if (!ReferenceEquals(hit, _hovered))
        {
            _hovered = hit;
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        if (_hovered is not null) { _hovered = null; InvalidateVisual(); }
    }

    private bool TryGetRect(FileSystemNode node, out Rect rect)
    {
        for (int i = _rects.Count - 1; i >= 0; i--)
            if (ReferenceEquals(_rects[i].Node, node)) { rect = _rects[i].Bounds; return true; }
        rect = default;
        return false;
    }

    // ---------------- Squarified ----------------

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
}
