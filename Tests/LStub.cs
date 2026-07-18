namespace ObsidianDisk;

/// <summary>
/// Stub de localização para os testes. No app real, <c>L</c> vive em App.xaml.cs e lê
/// recursos WPF; aqui os Services puros só precisam que ele não quebre. Devolve a chave
/// (ou o texto formatado) sem depender de <c>Application.Current</c>.
/// </summary>
internal static class L
{
    public static string T(string key) => key;

    public static string F(string key, params object?[] args)
    {
        try { return string.Format(key, args); }
        catch (System.FormatException) { return key; }
    }
}
