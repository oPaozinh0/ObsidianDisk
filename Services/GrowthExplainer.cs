using ObsidianDisk.Models;

namespace ObsidianDisk.Services;

/// <summary>
/// Traduz o diff entre dois snapshots numa explicação em linguagem natural — "por que meu
/// disco encheu?". Motor de templates puro (offline, sem IA externa), rotulando os culpados
/// com o <see cref="SafetyDatabase"/>.
/// </summary>
public static class GrowthExplainer
{
    private const long StableThreshold = 50L * 1024 * 1024; // < 50 MB: praticamente estável

    /// <summary>Resumo em uma ou duas frases. Null se não houver dados suficientes.</summary>
    public static string? Explain(TreeSnapshot older, TreeSnapshot newer, IReadOnlyList<FolderDelta> diff)
    {
        long total = newer.TotalBytes - older.TotalBytes;
        string since = older.Timestamp.ToString("dd/MM");
        var parts = new List<string>();

        if (Math.Abs(total) < StableThreshold)
        {
            parts.Add(L.F("Ex.Stable", since));
        }
        else if (total > 0)
        {
            parts.Add(L.F("Ex.Grew", since, FileSystemNode.FormatSize(total)));

            var growers = diff.Where(d => d.Delta > 0).Take(3).ToList();
            if (growers.Count > 0)
            {
                var names = growers.Select(g => $"{g.Name} (+{FileSystemNode.FormatSize(g.Delta)})");
                parts.Add(L.F("Ex.MainFrom", string.Join(", ", names)));

                // Dica se o maior culpado é seguro limpar
                if (SafetyDatabase.LookupPath(growers[0].FullPath, isDirectory: true) is { Level: SafetyLevel.Safe })
                    parts.Add(L.F("Ex.SafeHint", growers[0].Name));
            }
        }
        else
        {
            parts.Add(L.F("Ex.Freed", since, FileSystemNode.FormatSize(-total)));

            var shrinkers = diff.Where(d => d.Delta < 0).Take(2).ToList();
            if (shrinkers.Count > 0)
                parts.Add(L.F("Ex.MostlyIn", string.Join(", ", shrinkers.Select(s => s.Name))));
        }

        return parts.Count > 0 ? string.Join(" ", parts) : null;
    }
}
