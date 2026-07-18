using System.Diagnostics;

namespace ObsidianDisk.Services;

/// <summary>
/// Registra (ou remove) uma tarefa diária no Agendador do Windows que roda o modo headless
/// para escanear um caminho e gravar um relatório. Usa <c>schtasks.exe</c> — sem COM, sem
/// dependências. Uma tarefa por usuário; requer apenas privilégios normais.
/// </summary>
public static class ScheduledScan
{
    private const string TaskName = "ObsidianDisk Daily Scan";

    private static string ExePath => Environment.ProcessPath ?? "";

    public static bool IsRegistered() => RunSchTasks("/Query", "/TN", TaskName) == 0;

    /// <summary>Cria/atualiza a tarefa diária. <paramref name="time"/> em HH:mm.</summary>
    public static bool Register(string scanPath, string reportPath, string time = "12:00")
    {
        if (string.IsNullOrEmpty(ExePath)) return false;

        // O /TR precisa ser um único argumento com as aspas internas preservadas.
        // ArgumentList cuida do escape externo para o CreateProcess.
        string command = $"\"{ExePath}\" --scan \"{scanPath}\" --report \"{reportPath}\"";
        return RunSchTasks(
            "/Create", "/F",
            "/SC", "DAILY",
            "/TN", TaskName,
            "/TR", command,
            "/ST", time) == 0;
    }

    public static bool Unregister() => RunSchTasks("/Delete", "/F", "/TN", TaskName) == 0;

    private static int RunSchTasks(params string[] arguments)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var arg in arguments) psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi);
            if (process is null) return -1;
            process.WaitForExit();
            return process.ExitCode;
        }
        catch
        {
            return -1;
        }
    }
}
