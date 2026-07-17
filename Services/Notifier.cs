namespace ObsidianDisk.Services;

/// <summary>
/// Fachada estática para notificações nativas. Desacopla o código de feature (alerta de
/// disco cheio, regras automáticas) do <see cref="TrayService"/>, que detém o NotifyIcon.
/// Se nenhuma bandeja estiver ativa, a chamada é simplesmente ignorada.
/// </summary>
public static class Notifier
{
    private static TrayService? _tray;

    internal static void Register(TrayService tray) => _tray = tray;

    /// <summary>Mostra uma notificação nativa, se houver bandeja disponível.</summary>
    public static void Show(string title, string message) => _tray?.Notify(title, message);
}
