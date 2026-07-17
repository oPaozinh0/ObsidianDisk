using System.Globalization;
using System.Windows;
using ObsidianDisk.Services;

namespace ObsidianDisk;

public partial class App : Application
{
    private static readonly string[] SupportedLanguages = { "pt", "en", "es", "fr", "de" };

    /// <summary>Tema resolvido na inicialização (lido por controles desenhados em código).</summary>
    public static bool IsLightTheme { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        var settings = AppStorage.LoadSettings();

        // ---- Tema (o dicionário precisa entrar ANTES de qualquer estilo ser usado) ----
        IsLightTheme = settings.Theme.Equals("light", StringComparison.OrdinalIgnoreCase);
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"Themes/{(IsLightTheme ? "Light" : "Dark")}.xaml", UriKind.Relative),
        });

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

        base.OnStartup(e);
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
