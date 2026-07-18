using System.Globalization;
using System.Windows;
using ObsidianDisk.Services;

namespace ObsidianDisk;

public partial class App : Application
{
    private static readonly string[] SupportedLanguages = { "pt", "en", "es", "fr", "de" };

    /// <summary>Tema em vigor (lido por controles desenhados em código).</summary>
    public static bool IsLightTheme { get; private set; }

    private static int _themeIndex = -1;

    /// <summary>Troca o tema em tempo real: dicionário + paletas dos controles desenhados à mão.</summary>
    public static void ApplyTheme(bool light)
    {
        IsLightTheme = light;
        var dict = new ResourceDictionary
        {
            Source = new Uri($"Themes/{(light ? "Light" : "Dark")}.xaml", UriKind.Relative),
        };

        if (_themeIndex >= 0) Current.Resources.MergedDictionaries[_themeIndex] = dict;
        else { Current.Resources.MergedDictionaries.Insert(0, dict); _themeIndex = 0; }

        // Os controles desenhados em OnRender não usam DynamicResource: recalcule na mão
        Controls.ObsidianButton.RebuildPalette();
        Controls.TreemapControl.RebuildPalette();
    }

    /// <summary>Aplica a paleta daltônica em tempo real.</summary>
    public static void ApplyColorBlind(bool safe)
    {
        FileCategories.ColorBlindSafe = safe;
        Controls.TreemapControl.RebuildPalette();
    }


    protected override void OnStartup(StartupEventArgs e)
    {
        var settings = AppStorage.LoadSettings();

        FileCategories.ColorBlindSafe = settings.ColorBlindSafe;

        // Alguns drivers (AMD/adaptadores virtuais) deixam artefatos ao redor dos glifos
        // no pipeline de GPU. Render por software é o único caminho 100% limpo.
        if (settings.SoftwareRendering || Environment.GetEnvironmentVariable("OBS_SOFT") == "1")
            System.Windows.Media.RenderOptions.ProcessRenderMode =
                System.Windows.Interop.RenderMode.SoftwareOnly;

        // ---- Tema (o dicionário precisa entrar ANTES de qualquer estilo ser usado) ----
        ApplyTheme(settings.Theme.Equals("light", StringComparison.OrdinalIgnoreCase));

        // ---- Idioma: configuração > argumento --lang > idioma do Windows ----
        string lang = settings.Language;
        var args = Environment.GetCommandLineArgs();
        int langIndex = Array.FindIndex(args, a => a.Equals("--lang", StringComparison.OrdinalIgnoreCase));
        if (langIndex >= 0 && langIndex + 1 < args.Length)
            lang = args[langIndex + 1];

        if (lang == "auto" || !SupportedLanguages.Contains(lang, StringComparer.OrdinalIgnoreCase))
            lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
        if (!SupportedLanguages.Contains(lang))
            lang = "en";

        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"Resources/Strings.{lang}.xaml", UriKind.Relative),
        });

        // Modo headless (agendador / linha de comando): escaneia e gera relatório, sem janela.
        // Ex.: ObsidianDisk.exe --scan C:\ --report saida.html
        string? scanPath = GetArg(args, "--scan");
        if (scanPath is not null)
        {
            int code = HeadlessRunner.Run(scanPath, GetArg(args, "--report"));
            Shutdown(code);
            return; // não chama base.OnStartup → a StartupUri (janela) nunca é criada
        }

        base.OnStartup(e);
    }

    /// <summary>Valor que segue uma flag na linha de comando (ex.: "--scan C:\"), ou null.</summary>
    private static string? GetArg(string[] args, string name)
    {
        int i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    /// <summary>Reinicia o aplicativo (usado ao trocar idioma/tema).</summary>
    public static void Restart()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = Environment.ProcessPath!,
            UseShellExecute = true,
        });
        Current.Shutdown();
    }
}

/// <summary>Acesso às strings localizadas a partir do code-behind.</summary>
public static class L
{
    public static string T(string key) =>
        Application.Current.TryFindResource(key) as string ?? key;

    public static string F(string key, params object?[] args) =>
        string.Format(T(key), args);
}
