using System.IO;
using System.Text.Json;

namespace ObsidianDisk.Services;

public sealed class AppSettings
{
    public bool ConfirmDelete { get; set; } = true;
    public long LargeFileMinBytes { get; set; } = 100L * 1024 * 1024; // 100 MB
    public bool LiveMonitoring { get; set; } = true;
    public string Language { get; set; } = "auto"; // auto | pt | en | es | fr | de
    public string Theme { get; set; } = "dark";    // dark | light
    public bool ColorBlindSafe { get; set; }
    public bool SoftwareRendering { get; set; }
    public bool MinimizeToTray { get; set; } // minimizar esconde na bandeja em vez da barra de tarefas
    public bool DiskFullAlert { get; set; } = true;   // notificar quando o disco cruza o limite / projeção
    public int DiskFullThresholdPercent { get; set; } = 90;
}

public sealed record ScanRecord(DateTime Timestamp, string Path, long TotalBytes, long FileCount);

/// <summary>Persistência simples (JSON) em %LocalAppData%\ObsidianDisk.</summary>
public static class AppStorage
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ObsidianDisk");

    private static readonly string SettingsFile = Path.Combine(Dir, "settings.json");
    private static readonly string HistoryFile = Path.Combine(Dir, "history.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFile))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public static void SaveSettings(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(settings, JsonOpts));
        }
        catch { }
    }

    public static List<ScanRecord> LoadHistory()
    {
        try
        {
            if (File.Exists(HistoryFile))
                return JsonSerializer.Deserialize<List<ScanRecord>>(File.ReadAllText(HistoryFile)) ?? new();
        }
        catch { }
        return new();
    }

    public static void AppendHistory(ScanRecord record)
    {
        try
        {
            var history = LoadHistory();
            history.Add(record);
            if (history.Count > 500) // limite de segurança
                history.RemoveRange(0, history.Count - 500);
            Directory.CreateDirectory(Dir);
            File.WriteAllText(HistoryFile, JsonSerializer.Serialize(history, JsonOpts));
        }
        catch { }
    }
}
