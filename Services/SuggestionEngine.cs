using ObsidianDisk.Models;

namespace ObsidianDisk.Services;

public enum SuggestionAction { OpenMap, OpenDiscoveries, OpenLargeFiles }

/// <summary>Uma sugestão acionável para o usuário, com o alvo da ação.</summary>
public sealed record Suggestion(string Text, SuggestionAction Action, FileSystemNode? Node);

/// <summary>
/// Sugestões personalizadas a partir do que o app já sabe: padrões recorrentes no histórico
/// de snapshots (pastas que sempre crescem) e sinais da árvore atual (dev junk, arquivos
/// grandes esquecidos). Puro e offline.
/// </summary>
public static class SuggestionEngine
{
    private const long TreeSignalMin = 2L << 30; // 2 GB para sinais da árvore atual
    private const int MinSnapshots = 3;          // mínimo de scans para detectar tendência
    private const int LookbackSnapshots = 5;

    public static List<Suggestion> Build(FileSystemNode root, string path)
    {
        var result = new List<Suggestion>();

        // 1. Padrão recorrente: uma pasta que cresce em vários scans seguidos.
        if (RecurringGrower(path) is { } g)
            result.Add(new Suggestion(
                L.F("Sg.Grower", g.Name, FileSystemNode.FormatSize(g.Total), g.Intervals + 1),
                SuggestionAction.OpenMap, root.FindByPath(g.FullPath)));

        // 2. Artefatos de desenvolvimento acumulados.
        long dev = DiscoveryAnalyzer.DevJunk(root).Sum(d => d.Size);
        if (dev >= TreeSignalMin)
            result.Add(new Suggestion(
                L.F("Sg.DevJunk", FileSystemNode.FormatSize(dev)), SuggestionAction.OpenDiscoveries, null));

        // 3. Arquivos grandes que não são abertos há mais de um ano.
        long old = DiscoveryAnalyzer
            .LargeFilesByAge(root, DateTime.UtcNow.AddDays(-365), 100L << 20)
            .Sum(d => d.Size);
        if (old >= TreeSignalMin)
            result.Add(new Suggestion(
                L.F("Sg.OldFiles", FileSystemNode.FormatSize(old)), SuggestionAction.OpenLargeFiles, null));

        return result;
    }

    private readonly record struct Grower(string FullPath, string Name, long Total, int Intervals);

    /// <summary>Pasta que cresceu no maior número de intervalos recentes (>= 2), com maior ganho total.</summary>
    private static Grower? RecurringGrower(string path)
    {
        var snaps = SnapshotStore.RecentForPath(path, LookbackSnapshots);
        if (snaps.Count < MinSnapshots) return null;

        var intervals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var totals = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        for (var i = 1; i < snaps.Count; i++)
            foreach (var d in SnapshotStore.Diff(snaps[i - 1], snaps[i]))
                if (d.Delta > 0)
                {
                    intervals[d.FullPath] = intervals.GetValueOrDefault(d.FullPath) + 1;
                    totals[d.FullPath] = totals.GetValueOrDefault(d.FullPath) + d.Delta;
                }

        string? best = null;
        foreach (var kv in intervals)
            if (kv.Value >= 2 &&
                (best is null || kv.Value > intervals[best] ||
                 (kv.Value == intervals[best] && totals[kv.Key] > totals[best])))
                best = kv.Key;

        if (best is null) return null;
        var name = System.IO.Path.GetFileName(best.TrimEnd('\\', '/'));
        return new Grower(best, string.IsNullOrEmpty(name) ? best : name, totals[best], intervals[best]);
    }
}
