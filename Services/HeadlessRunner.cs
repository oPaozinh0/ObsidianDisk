using System.IO;
using System.Text;
using System.Text.Json;
using ObsidianDisk.Models;

namespace ObsidianDisk.Services;

/// <summary>
/// Execução sem interface: escaneia um caminho, registra no histórico/snapshots e grava um
/// relatório (HTML ou JSON, pela extensão). Base do agendador e de integrações via linha de
/// comando: <c>ObsidianDisk.exe --scan C:\ --report saida.html</c>.
/// </summary>
public static class HeadlessRunner
{
    /// <summary>Códigos de saída: 0 ok · 1 erro · 2 caminho inválido.</summary>
    public static int Run(string scanPath, string? reportPath)
    {
        try
        {
            if (!Directory.Exists(scanPath)) return 2;

            var scanner = new DiskScanner();
            var (root, task) = scanner.StartScan(scanPath, CancellationToken.None);
            task.Wait(); // headless: pode bloquear, não há UI para travar
            root.SortBySizeDescending();

            var now = DateTime.Now;
            long fileCount = scanner.Progress.FilesScanned;

            // Alimenta histórico/snapshots — assim scans agendados constroem a tendência
            AppStorage.AppendHistory(new ScanRecord(now, scanPath, root.Size, fileCount));
            SnapshotStore.Capture(root, scanPath, now, fileCount);

            if (!string.IsNullOrWhiteSpace(reportPath))
                WriteReport(reportPath!, scanPath, root, now, fileCount);

            return 0;
        }
        catch
        {
            return 1;
        }
    }

    private static void WriteReport(string reportPath, string scanPath, FileSystemNode root,
        DateTime now, long fileCount)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(reportPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var snapshot = SnapshotStore.Build(root, scanPath, now, fileCount);

        if (Path.GetExtension(reportPath).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(reportPath, json, Encoding.UTF8);
        }
        else
        {
            var history = AppStorage.LoadHistory()
                .Where(r => string.Equals(r.Path, scanPath, StringComparison.OrdinalIgnoreCase))
                .ToList();
            File.WriteAllText(reportPath, ReportExporter.BuildHtml(scanPath, history, snapshot, root), Encoding.UTF8);
        }
    }
}
