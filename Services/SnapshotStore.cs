using System.IO;
using System.Text.Json;
using ObsidianDisk.Models;

namespace ObsidianDisk.Services;

/// <summary>Uma pasta e seu tamanho, num retrato de scan.</summary>
public sealed record SnapshotEntry(string FullPath, long Size);

/// <summary>Retrato leve de um scan: total do disco e as maiores pastas por tamanho.</summary>
public sealed record TreeSnapshot(
    DateTime Timestamp,
    string Path,
    long TotalBytes,
    long FileCount,
    IReadOnlyList<SnapshotEntry> TopFolders);

/// <summary>Variação de tamanho de uma pasta entre dois snapshots.</summary>
public sealed record FolderDelta(string FullPath, string Name, long OldSize, long NewSize, long Delta);

/// <summary>
/// Persiste um retrato leve da árvore ao fim de cada scan — apenas as maiores pastas,
/// não a árvore inteira (poucos KB por scan). É a base para diff entre scans, o
/// explicador de crescimento, sugestões personalizadas e a timeline de espaço.
/// </summary>
public static class SnapshotStore
{
    private const int TopFolderCount = 200; // maiores pastas guardadas por scan
    private const int MaxSnapshots = 100;   // arquivos de snapshot retidos (limite de segurança)

    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ObsidianDisk", "snapshots");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    /// <summary>
    /// Constrói e grava um snapshot a partir da raiz escaneada. Faz um percurso da árvore
    /// e I/O — chame fora da thread de UI (ex.: dentro de um Task.Run).
    /// </summary>
    public static void Capture(FileSystemNode root, string path, DateTime timestamp, long fileCount)
    {
        try
        {
            var folders = new List<SnapshotEntry>();
            CollectFolders(root, folders);
            folders.Sort(static (a, b) => b.Size.CompareTo(a.Size));
            if (folders.Count > TopFolderCount)
                folders.RemoveRange(TopFolderCount, folders.Count - TopFolderCount);

            var snapshot = new TreeSnapshot(timestamp, path, root.Size, fileCount, folders);

            Directory.CreateDirectory(Dir);
            var file = Path.Combine(Dir, $"snapshot-{timestamp:yyyyMMddHHmmss}.json");
            File.WriteAllText(file, JsonSerializer.Serialize(snapshot, JsonOpts));

            Prune();
        }
        catch { }
    }

    /// <summary>Arquivos de snapshot existentes, do mais antigo ao mais recente.</summary>
    public static List<string> ListFiles()
    {
        try
        {
            if (!Directory.Exists(Dir)) return new();
            var files = Directory.GetFiles(Dir, "snapshot-*.json");
            Array.Sort(files, StringComparer.Ordinal); // o nome codifica o timestamp: ordinal = cronológico
            return new List<string>(files);
        }
        catch { return new(); }
    }

    public static TreeSnapshot? Load(string file)
    {
        try
        {
            return JsonSerializer.Deserialize<TreeSnapshot>(File.ReadAllText(file));
        }
        catch { return null; }
    }

    /// <summary>Carrega os N snapshots mais recentes, do mais antigo ao mais recente.</summary>
    public static List<TreeSnapshot> LoadRecent(int count)
    {
        var files = ListFiles();
        var start = Math.Max(0, files.Count - count);
        var result = new List<TreeSnapshot>();
        for (var i = start; i < files.Count; i++)
            if (Load(files[i]) is { } snap)
                result.Add(snap);
        return result;
    }

    /// <summary>Até <paramref name="count"/> snapshots mais recentes de um caminho, do mais antigo ao mais novo.</summary>
    public static List<TreeSnapshot> RecentForPath(string path, int count)
    {
        var files = ListFiles(); // cronológico
        var result = new List<TreeSnapshot>();
        for (var i = files.Count - 1; i >= 0 && result.Count < count; i--)
        {
            var s = Load(files[i]);
            if (s is not null && string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase))
                result.Add(s);
        }
        result.Reverse(); // mais antigo primeiro
        return result;
    }

    /// <summary>Os dois snapshots mais recentes de um caminho (mais antigo, mais novo).</summary>
    public static (TreeSnapshot? Older, TreeSnapshot? Newer) TwoLatestForPath(string path)
    {
        var files = ListFiles(); // cronológico
        TreeSnapshot? newer = null, older = null;
        for (var i = files.Count - 1; i >= 0; i--)
        {
            var s = Load(files[i]);
            if (s is null || !string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase)) continue;
            if (newer is null) { newer = s; continue; }
            older = s;
            break;
        }
        return (older, newer);
    }

    /// <summary>
    /// Variação de tamanho por pasta entre dois snapshots, maior variação (em módulo) primeiro.
    /// Aproximado: cada snapshot guarda só as maiores pastas, então uma pasta ausente conta como 0.
    /// </summary>
    public static List<FolderDelta> Diff(TreeSnapshot older, TreeSnapshot newer)
    {
        var oldMap = older.TopFolders.ToDictionary(f => f.FullPath, f => f.Size, StringComparer.OrdinalIgnoreCase);
        var newMap = newer.TopFolders.ToDictionary(f => f.FullPath, f => f.Size, StringComparer.OrdinalIgnoreCase);

        var keys = new HashSet<string>(oldMap.Keys, StringComparer.OrdinalIgnoreCase);
        keys.UnionWith(newMap.Keys);

        var result = new List<FolderDelta>();
        foreach (var key in keys)
        {
            long o = oldMap.GetValueOrDefault(key);
            long n = newMap.GetValueOrDefault(key);
            if (n == o) continue;
            var name = Path.GetFileName(key.TrimEnd('\\', '/'));
            result.Add(new FolderDelta(key, string.IsNullOrEmpty(name) ? key : name, o, n, n - o));
        }
        result.Sort((a, b) => Math.Abs(b.Delta).CompareTo(Math.Abs(a.Delta)));
        return result;
    }

    private static void CollectFolders(FileSystemNode node, List<SnapshotEntry> into)
    {
        foreach (var child in node.Children)
        {
            if (!child.IsDirectory) continue;
            into.Add(new SnapshotEntry(child.FullPath, child.Size));
            CollectFolders(child, into);
        }
    }

    private static void Prune()
    {
        var files = ListFiles();
        if (files.Count <= MaxSnapshots) return;
        for (var i = 0; i < files.Count - MaxSnapshots; i++)
        {
            try { File.Delete(files[i]); } catch { }
        }
    }
}
