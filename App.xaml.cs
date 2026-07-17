using System.Globalization;
using System.Windows;

namespace ObsidianDisk;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Idioma: português quando o Windows está em pt-*, inglês caso contrário.
        // "--lang pt|en" força um idioma específico.
        bool pt = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            .Equals("pt", StringComparison.OrdinalIgnoreCase);

        var args = Environment.GetCommandLineArgs();
        int langIndex = Array.FindIndex(args, a => a.Equals("--lang", StringComparison.OrdinalIgnoreCase));
        if (langIndex >= 0 && langIndex + 1 < args.Length)
            pt = args[langIndex + 1].StartsWith("pt", StringComparison.OrdinalIgnoreCase);

        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"Resources/Strings.{(pt ? "pt" : "en")}.xaml", UriKind.Relative),
        });

        base.OnStartup(e);
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
