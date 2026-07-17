using System.Globalization;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace ObsidianDisk.Controls;

public enum ObsidianButtonVariant { Primary, Ghost, Danger }

/// <summary>
/// Botão desenhado do zero via OnRender. O rótulo vira geometria vetorial
/// (FormattedText.BuildGeometry), o que evita os artefatos de glyph-run do
/// pipeline de GPU em alguns drivers. Assinatura visual: canto inferior
/// direito facetado (chanfro com brilho), ecoando a gema do logotipo.
/// </summary>
public class ObsidianButton : System.Windows.Controls.Button
{
    private const double CornerRadius = 6;
    private const double FacetCut = 10;

    public static readonly DependencyProperty VariantProperty = DependencyProperty.Register(
        nameof(Variant), typeof(ObsidianButtonVariant), typeof(ObsidianButton),
        new FrameworkPropertyMetadata(ObsidianButtonVariant.Ghost,
            FrameworkPropertyMetadataOptions.AffectsRender));

    public ObsidianButtonVariant Variant
    {
        get => (ObsidianButtonVariant)GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    // ---- Paleta (congelada uma vez) ----
    private static SolidColorBrush B(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    // Recalculadas quando o tema muda (ver RebuildPalette)
    private static SolidColorBrush AccentBg = null!, AccentHover = null!, AccentPressed = null!;
    private static SolidColorBrush GhostBg = null!, GhostHover = null!, GhostPressed = null!;
    private static SolidColorBrush TextNormal = null!, TextMuted = null!, TextDanger = null!;

    private static readonly SolidColorBrush DangerBg = B(0x00, 0x00, 0x00, 0x00);
    private static readonly SolidColorBrush DangerHover = B(0x33, 0xD6, 0x45, 0x50);
    private static readonly SolidColorBrush DangerPressed = B(0x55, 0xD6, 0x45, 0x50);
    private static readonly SolidColorBrush TextWhite = B(0xFF, 0xFF, 0xFF, 0xFF);

    private static Pen P(byte a, byte r, byte g, byte b, double thickness = 1)
    {
        var pen = new Pen(B(a, r, g, b), thickness);
        pen.Freeze();
        return pen;
    }

    private static Pen StrokePen = null!, StrokeHoverPen = null!, DangerPen = null!;
    private static Pen FacetGhost = null!, FacetDanger = null!;

    private static readonly Pen FacetLight = P(0x66, 0xFF, 0xFF, 0xFF, 1.4);
    private static readonly Pen FocusPen = FrozenDashPen();

    private static Pen FrozenDashPen()
    {
        var pen = new Pen(B(0xAA, 0x96, 0x78, 0xFF), 1) { DashStyle = DashStyles.Dash };
        pen.Freeze();
        return pen;
    }

    static ObsidianButton() => RebuildPalette();

    /// <summary>Recalcula as cores após uma troca de tema (chamado por App.ApplyTheme).</summary>
    public static void RebuildPalette()
    {
        bool light = App.IsLightTheme;

        AccentBg = light ? B(0xFF, 0x6C, 0x4C, 0xF0) : B(0xFF, 0x7C, 0x5C, 0xFF);
        AccentHover = light ? B(0xFF, 0x5A, 0x3B, 0xD6) : B(0xFF, 0x96, 0x78, 0xFF);
        AccentPressed = light ? B(0xFF, 0x4A, 0x2F, 0xC0) : B(0xFF, 0x67, 0x49, 0xD8);
        GhostBg = light ? B(0xFF, 0xE9, 0xEB, 0xF2) : B(0xFF, 0x1C, 0x22, 0x30);
        GhostHover = light ? B(0xFF, 0xDF, 0xE3, 0xEE) : B(0xFF, 0x24, 0x2C, 0x3E);
        GhostPressed = light ? B(0xFF, 0xD3, 0xD8, 0xE6) : B(0xFF, 0x16, 0x1B, 0x27);
        TextNormal = light ? B(0xFF, 0x1B, 0x21, 0x30) : B(0xFF, 0xE6, 0xEA, 0xF2);
        TextMuted = light ? B(0xFF, 0x9A, 0xA1, 0xB5) : B(0xFF, 0x8A, 0x93, 0xA8);
        TextDanger = light ? B(0xFF, 0xC0, 0x35, 0x40) : B(0xFF, 0xE0, 0x6C, 0x75);

        StrokePen = light ? P(0xFF, 0xD5, 0xD9, 0xE4) : P(0xFF, 0x2A, 0x32, 0x45);
        StrokeHoverPen = light ? P(0xFF, 0x6C, 0x4C, 0xF0) : P(0xFF, 0x7C, 0x5C, 0xFF);
        DangerPen = light ? P(0xAA, 0xC0, 0x35, 0x40) : P(0xAA, 0xD6, 0x45, 0x50);
        FacetGhost = light ? P(0x50, 0x6C, 0x4C, 0xF0, 1.4) : P(0x50, 0x7C, 0x5C, 0xFF, 1.4);
        FacetDanger = light ? P(0x70, 0xC0, 0x35, 0x40, 1.4) : P(0x70, 0xE0, 0x6C, 0x75, 1.4);
    }

    private FormattedText? _label;
    private Geometry? _labelGeometry;
    private string _labelText = "";

    public ObsidianButton()
    {
        Cursor = Cursors.Hand;
        Padding = new Thickness(16, 7, 16, 7);
        FontSize = 13;
        FontWeight = FontWeights.SemiBold;
        ClipToBounds = false;
        Template = null;          // sem template do tema: todo o visual sai do OnRender
        FocusVisualStyle = null;  // o anel de foco é desenhado por nós
    }

    // ---- Medição: tamanho do rótulo + padding ----

    private void EnsureLabel()
    {
        string text = Content?.ToString() ?? "";
        if (_label is not null && text == _labelText) return;

        _labelText = text;
        _label = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyles.Normal, FontWeight, FontStretches.Normal),
            FontSize, TextWhite, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        _labelGeometry = null; // regenera no render (depende da posição)
    }

    protected override Size MeasureOverride(Size constraint)
    {
        EnsureLabel();
        double w = (_label?.Width ?? 0) + Padding.Left + Padding.Right;
        double h = (_label?.Height ?? 0) + Padding.Top + Padding.Bottom;
        return new Size(Math.Min(constraint.Width, Math.Max(w, MinWidth)),
                        Math.Min(constraint.Height, Math.Max(h, MinHeight)));
    }

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        _label = null;
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsMouseOverProperty || e.Property == IsPressedProperty ||
            e.Property == IsEnabledProperty || e.Property == IsKeyboardFocusedProperty)
            InvalidateVisual();
    }

    // ---- Desenho ----

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 4 || h < 4) return;

        var (bg, border, textBrush, facet) = ResolveLook();

        var shape = BuildFacetedShape(w, h);
        dc.DrawGeometry(bg, border, shape);

        // brilho do chanfro: o "corte de gema" no canto inferior direito
        if (facet is not null && IsEnabled)
            dc.DrawLine(facet, new Point(w - 0.5, h - FacetCut), new Point(w - FacetCut, h - 0.5));

        if (IsKeyboardFocused)
        {
            var inset = BuildFacetedShape(w - 5, h - 5);
            dc.PushTransform(new TranslateTransform(2.5, 2.5));
            dc.DrawGeometry(null, FocusPen, inset);
            dc.Pop();
        }

        // rótulo como geometria vetorial (imune a artefatos de glyph-run)
        EnsureLabel();
        if (_label is null || _label.Width <= 0) return;

        double x = Math.Round((w - _label.Width) / 2);
        double y = Math.Round((h - _label.Height) / 2);
        double press = IsPressed ? 1 : 0;

        _labelGeometry ??= _label.BuildGeometry(new Point(0, 0));
        dc.PushTransform(new TranslateTransform(x, y + press));
        dc.DrawGeometry(textBrush, null, _labelGeometry);
        dc.Pop();
    }

    private (Brush bg, Pen? border, Brush text, Pen? facet) ResolveLook()
    {
        if (!IsEnabled)
            return (GhostBg, StrokePen, TextMuted, null);

        return Variant switch
        {
            ObsidianButtonVariant.Primary => (
                IsPressed ? AccentPressed : IsMouseOver ? AccentHover : AccentBg,
                null, TextWhite, FacetLight),

            ObsidianButtonVariant.Danger => (
                IsPressed ? DangerPressed : IsMouseOver ? DangerHover : DangerBg,
                DangerPen, TextDanger, FacetDanger),

            _ => (
                IsPressed ? GhostPressed : IsMouseOver ? GhostHover : GhostBg,
                IsMouseOver ? StrokeHoverPen : StrokePen, TextNormal, FacetGhost),
        };
    }

    /// <summary>Retângulo com três cantos arredondados e o inferior direito chanfrado.</summary>
    private static StreamGeometry BuildFacetedShape(double w, double h)
    {
        double r = CornerRadius, c = FacetCut;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(r, 0.5), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(w - r, 0.5), true, false);
            ctx.ArcTo(new Point(w - 0.5, r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(w - 0.5, h - c), true, false);
            ctx.LineTo(new Point(w - c, h - 0.5), true, false); // chanfro
            ctx.LineTo(new Point(r, h - 0.5), true, false);
            ctx.ArcTo(new Point(0.5, h - r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(0.5, r), true, false);
            ctx.ArcTo(new Point(r, 0.5), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
        }
        geo.Freeze();
        return geo;
    }
}
