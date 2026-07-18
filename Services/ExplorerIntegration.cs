using Microsoft.Win32;

namespace ObsidianDisk.Services;

/// <summary>
/// Registra (ou remove) a entrada "Analisar com ObsidianDisk" no menu de contexto de pastas
/// e drives do Explorer. Escreve só em HKCU (não precisa de administrador). O app já aceita
/// o caminho por argumento ("ObsidianDisk.exe &lt;pasta&gt;"), então o comando é apenas o exe + "%1".
/// </summary>
public static class ExplorerIntegration
{
    private const string KeyName = "ObsidianDisk";

    // "Directory" = clicar numa pasta; "Drive" = clicar numa unidade (C:, D:…)
    private static readonly string[] ShellRoots =
    {
        @"Software\Classes\Directory\shell",
        @"Software\Classes\Drive\shell",
    };

    private static string ExePath => Environment.ProcessPath ?? "";

    /// <summary>True se a entrada já aponta para este executável.</summary>
    public static bool IsRegistered()
    {
        try
        {
            using var cmd = Registry.CurrentUser.OpenSubKey($@"{ShellRoots[0]}\{KeyName}\command");
            return cmd?.GetValue(null) is string value &&
                   value.Contains(ExePath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Cria/atualiza a entrada em todos os locais. Retorna true em caso de sucesso.</summary>
    public static bool Register(string menuLabel)
    {
        if (string.IsNullOrEmpty(ExePath)) return false;
        try
        {
            foreach (var root in ShellRoots)
            {
                using var shell = Registry.CurrentUser.CreateSubKey($@"{root}\{KeyName}");
                shell.SetValue(null, menuLabel);          // rótulo mostrado no menu
                shell.SetValue("Icon", $"\"{ExePath}\"");  // ícone = o próprio exe
                using var command = shell.CreateSubKey("command");
                command.SetValue(null, $"\"{ExePath}\" \"%1\"");
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Remove a entrada de todos os locais. Retorna true se nada sobrou.</summary>
    public static bool Unregister()
    {
        try
        {
            foreach (var root in ShellRoots)
                Registry.CurrentUser.DeleteSubKeyTree($@"{root}\{KeyName}", throwOnMissingSubKey: false);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
