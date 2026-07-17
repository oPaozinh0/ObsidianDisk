using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ObsidianDisk.Controls;

/// <summary>
/// Microinterações do app. Tudo aqui respeita a preferência do Windows de
/// "mostrar animações" — quem desligou (ou usa movimento reduzido) recebe o
/// estado final na hora, sem transição.
/// </summary>
public static class Animate
{
    /// <summary>Windows: Configurações → Acessibilidade → Efeitos visuais → Efeitos de animação.</summary>
    public static bool Enabled => SystemParameters.ClientAreaAnimation;

    private static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };

    /// <summary>Entrada de página: surge subindo alguns pixels.</summary>
    public static void PageIn(FrameworkElement element)
    {
        if (!Enabled)
        {
            element.Opacity = 1;
            element.RenderTransform = null;
            return;
        }

        var slide = new TranslateTransform(0, 10);
        element.RenderTransform = slide;
        element.Opacity = 0;

        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = Ease });
        slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = Ease });
    }

    /// <summary>Aparecer suavemente (botões de update/desfazer, banners).</summary>
    public static void FadeIn(FrameworkElement element, double durationMs = 200)
    {
        element.Visibility = Visibility.Visible;
        if (!Enabled) { element.Opacity = 1; return; }

        element.Opacity = 0;
        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = Ease });
    }

    /// <summary>Sumir suavemente e então colapsar.</summary>
    public static void FadeOut(FrameworkElement element, double durationMs = 160)
    {
        if (!Enabled)
        {
            element.Visibility = Visibility.Collapsed;
            return;
        }

        var fade = new DoubleAnimation(element.Opacity, 0, TimeSpan.FromMilliseconds(durationMs));
        fade.Completed += (_, _) =>
        {
            element.Visibility = Visibility.Collapsed;
            element.Opacity = 1;
        };
        element.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>Números que contam até o valor (uso do disco, percentuais).</summary>
    public static void CountTo(System.Windows.Controls.TextBlock text, double from, double to,
        Func<double, string> format, double durationMs = 700)
    {
        if (!Enabled) { text.Text = format(to); return; }

        var clock = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = Ease,
        };
        var helper = new AnimationHelper(v => text.Text = format(v));
        helper.BeginAnimation(AnimationHelper.ValueProperty, clock);
    }

    /// <summary>Barra que preenche até o valor.</summary>
    public static void ProgressTo(System.Windows.Controls.ProgressBar bar, double value, double durationMs = 700)
    {
        if (!Enabled) { bar.Value = value; return; }

        bar.BeginAnimation(System.Windows.Controls.ProgressBar.ValueProperty,
            new DoubleAnimation(0, value, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = Ease });
    }

    /// <summary>Ponte para animar um double arbitrário via callback.</summary>
    private sealed class AnimationHelper : Animatable
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(double), typeof(AnimationHelper),
            new PropertyMetadata(0.0, (d, e) => ((AnimationHelper)d)._onChanged((double)e.NewValue)));

        private readonly Action<double> _onChanged;

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public AnimationHelper(Action<double> onChanged) => _onChanged = onChanged;

        protected override Freezable CreateInstanceCore() => new AnimationHelper(_onChanged);
    }
}
