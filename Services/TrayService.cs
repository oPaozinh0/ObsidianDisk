using System.Windows;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace ObsidianDisk.Services;

/// <summary>
/// Ícone na bandeja do sistema + notificações nativas (balloon/toast), via NotifyIcon do
/// Windows Forms — parte do SDK Desktop, sem dependência externa nem NuGet. É a base para o
/// alerta de disco cheio, o "avisar" das regras automáticas e o monitoramento em segundo plano.
/// Deve ser criado na thread de UI.
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private bool _persistent; // ícone fixo na bandeja (opção "minimizar pra bandeja")

    /// <summary>Usuário pediu para reabrir a janela (duplo clique, balloon ou menu).</summary>
    public event Action? OpenRequested;

    /// <summary>Usuário pediu para sair de vez (menu da bandeja).</summary>
    public event Action? ExitRequested;

    public TrayService()
    {
        _icon = new Forms.NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "ObsidianDisk",
            Visible = false,
        };
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
        _icon.BalloonTipClicked += (_, _) => OpenRequested?.Invoke();
        _icon.BalloonTipClosed += (_, _) => { if (!_persistent) _icon.Visible = false; };

        var menu = new Forms.ContextMenuStrip();
        var open = new Forms.ToolStripMenuItem(L.T("Tray.Open"));
        open.Click += (_, _) => OpenRequested?.Invoke();
        var exit = new Forms.ToolStripMenuItem(L.T("Tray.Exit"));
        exit.Click += (_, _) => ExitRequested?.Invoke();
        menu.Items.Add(open);
        menu.Items.Add(exit);
        _icon.ContextMenuStrip = menu;

        Notifier.Register(this);
    }

    /// <summary>Liga/desliga o ícone fixo na bandeja (permanece enquanto o app roda).</summary>
    public void SetPersistent(bool on)
    {
        _persistent = on;
        _icon.Visible = on;
    }

    /// <summary>Texto do tooltip do ícone (ex.: "C:\ 82% usado"). O Windows limita a 63 caracteres.</summary>
    public void SetTooltip(string text) =>
        _icon.Text = text.Length > 63 ? text[..63] : text;

    /// <summary>Mostra uma notificação nativa. Torna o ícone visível pelo tempo do balloon.</summary>
    public void Notify(string title, string message)
    {
        if (!_icon.Visible) _icon.Visible = true; // balloon exige ícone visível
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = message;
        _icon.ShowBalloonTip(5000);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }

    private static Drawing.Icon LoadAppIcon()
    {
        try
        {
            var stream = Application.GetResourceStream(new Uri("Assets/app.ico", UriKind.Relative))?.Stream;
            if (stream is not null) return new Drawing.Icon(stream);
        }
        catch { }
        try { return Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)!; }
        catch { return Drawing.SystemIcons.Application; }
    }
}
