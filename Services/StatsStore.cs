using System.IO;
using System.Text.Json;

namespace ObsidianDisk.Services;

/// <summary>Estatísticas acumuladas de limpeza (gamificação leve).</summary>
public sealed record CleanupStats(long TotalFreedBytes, int Cleanups, int StreakDays, DateTime LastCleanupDate);

/// <summary>
/// Persiste o total de espaço recuperado, o nº de limpezas e a "sequência" (dias seguidos com
/// pelo menos uma limpeza). Guardado em %LocalAppData%\ObsidianDisk\stats.json.
/// </summary>
public static class StatsStore
{
    private static readonly string File_ = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ObsidianDisk", "stats.json");

    public static CleanupStats Load()
    {
        try
        {
            if (File.Exists(File_))
                return JsonSerializer.Deserialize<CleanupStats>(File.ReadAllText(File_))
                       ?? Empty;
        }
        catch { }
        return Empty;
    }

    /// <summary>Registra uma limpeza e devolve o estado atualizado.</summary>
    public static CleanupStats Record(long freedBytes)
    {
        var s = Load();
        var today = DateTime.Now.Date;

        int streak;
        if (s.LastCleanupDate == default) streak = 1;
        else
        {
            var last = s.LastCleanupDate.Date;
            if (last == today) streak = Math.Max(1, s.StreakDays);      // mesma data: mantém
            else if (last == today.AddDays(-1)) streak = s.StreakDays + 1; // ontem: soma
            else streak = 1;                                            // houve intervalo: reinicia
        }

        var updated = new CleanupStats(
            s.TotalFreedBytes + Math.Max(0, freedBytes),
            s.Cleanups + 1,
            streak,
            today);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File_)!);
            File.WriteAllText(File_, JsonSerializer.Serialize(updated));
        }
        catch { }

        return updated;
    }

    private static readonly CleanupStats Empty = new(0, 0, 0, default);
}
