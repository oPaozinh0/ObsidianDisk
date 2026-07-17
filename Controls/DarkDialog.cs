using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ObsidianDisk.Controls;

/// <summary>Diálogo de confirmação no tema escuro do app (substitui o MessageBox).</summary>
public static class DarkDialog
{
    /// <summary>Mostra uma confirmação. Retorna true se o usuário confirmar.</summary>
    public static bool Confirm(Window owner, string title, string message,
        bool danger = false, string confirmLabel = "Confirmar", string cancelLabel = "Cancelar")
    {
        var app = Application.Current;
        bool confirmed = false;

        var window = new Window
        {
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            MaxWidth = 520,
        };

        // Evita artefatos de ClearType no pipeline de GPU (mesma correção da MainWindow)
        System.Windows.Media.TextOptions.SetTextRenderingMode(window, System.Windows.Media.TextRenderingMode.Grayscale);
        System.Windows.Media.TextOptions.SetTextFormattingMode(window, System.Windows.Media.TextFormattingMode.Display);
        window.FontFamily = (FontFamily)app.FindResource("AppFont");

        var accent = (Brush)app.FindResource("Accent");
        var dangerBrush = new SolidColorBrush(Color.FromRgb(0xD6, 0x45, 0x50));

        // Título com ícone
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(new TextBlock
        {
            Text = (danger ? (char)0xE7BA : (char)0xE946).ToString(), // aviso / info (Segoe MDL2)
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 20,
            Foreground = danger ? dangerBrush : accent,
            VerticalAlignment = VerticalAlignment.Center,
        });
        titleRow.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = (Brush)app.FindResource("Text"),
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var messageText = new TextBlock
        {
            Text = message,
            Foreground = (Brush)app.FindResource("Muted"),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 20),
            LineHeight = 20,
        };

        var cancelButton = new ObsidianButton
        {
            Content = cancelLabel,
            Variant = ObsidianButtonVariant.Ghost,
            MinWidth = 110,
            Height = 36,
            IsCancel = true,
        };
        cancelButton.Click += (_, _) => window.Close();

        var confirmButton = new ObsidianButton
        {
            Content = confirmLabel,
            Variant = danger ? ObsidianButtonVariant.Danger : ObsidianButtonVariant.Primary,
            MinWidth = 110,
            Height = 36,
            Margin = new Thickness(10, 0, 0, 0),
        };
        confirmButton.Click += (_, _) => { confirmed = true; window.Close(); };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(confirmButton);

        var stack = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        stack.Children.Add(titleRow);
        stack.Children.Add(messageText);
        stack.Children.Add(buttons);

        var border = new Border
        {
            Background = (Brush)app.FindResource("Panel"),
            BorderBrush = danger ? dangerBrush : (Brush)app.FindResource("Stroke"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Child = stack,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 24, ShadowDepth = 4, Opacity = 0.5, Color = Colors.Black,
            },
            Margin = new Thickness(16), // espaço para a sombra
        };
        window.Content = border;

        // Esc cancela (IsCancel); Enter ativa o botão focado; arrastar pela área do diálogo
        border.MouseLeftButtonDown += (_, _) => window.DragMove();

        // Foco no botão seguro por padrão em ações perigosas
        window.Loaded += (_, _) => (danger ? cancelButton : confirmButton).Focus();

        window.ShowDialog();
        return confirmed;
    }
}
