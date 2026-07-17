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
