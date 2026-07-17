using System.IO;
using ObsidianDisk.Models;

namespace ObsidianDisk.Services;

/// <summary>Uma descoberta: pasta, arquivo ou agregado por extensão, com tamanho recuperável.</summary>
public sealed record DiscoveryItem(
    string Name,
    string Detail,        // caminho da pasta, ou "N arquivos" nos agregados
    DateTime WhenUtc,     // data relevante (modificação/acesso); default = sem data
    long Size,
    FileSystemNode? Node); // null nos agregados por extensão (não deletáveis diretamente)

/// <summary>
/// Análises sobre a árvore já escaneada: pastas esquecidas, extensões desperdiçadas,
/// artefatos de desenvolvimento e arquivos grandes por idade de acesso. Puro sobre os
/// dados em memória — reaproveita os metadados capturados pelo <see cref="DiskScanner"/>.
/// </summary>
public static class DiscoveryAnalyzer
{
    private const int MaxResults = 300;

    // Extensões cujo espaço costuma ser recuperável (logs, temporários, dumps, arquivões).
    private static readonly HashSet<string> WastedExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".log", ".tmp", ".temp", ".cache", ".bak", ".old", ".dmp", ".dump", ".chk",
        ".part", ".crdownload", ".download", ".iso", ".zip", ".rar", ".7z", ".gz", ".msi",
    };

    // Pastas de artefato de build/deps que podem ser regeneradas por ferramentas de dev.
    private static readonly HashSet<string> DevJunkDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "bower_components", ".venv", "venv", "__pycache__", ".gradle",
        "target", "build", "dist", ".next", ".nuxt", "obj", "bin", ".parcel-cache",
        "Pods", "DerivedData", ".tox", "vendor", ".pytest_cache", ".mypy_cache",
    };

    /// <summary>Pastas grandes não modificadas há muito tempo (a "raiz" antiga, sem subsumir).</summary>
    public static List<DiscoveryItem> GhostFolders(FileSystemNode root, DateTime cutoffUtc, long minBytes)
    {
        var found = new List<FileSystemNode>();

        void Walk(FileSystemNode dir)
        {
            foreach (var child in dir.Children)
            {
                if (!child.IsDirectory) continue;
                if (child.LastWriteUtc != default && child.LastWriteUtc < cutoffUtc && child.Size >= minBytes)
                    found.Add(child); // não desce: os filhos antigos estão incluídos aqui
                else
                    Walk(child);
            }
        }

        Walk(root);
        return found
            .OrderByDescending(n => n.Size)
            .Take(MaxResults)
            .Select(n => new DiscoveryItem(n.Name, Path.GetDirectoryName(n.FullPath) ?? "", n.LastWriteUtc, n.Size, n))
            .ToList();
    }

    /// <summary>Espaço somado por extensão "desperdiçável", maior primeiro.</summary>
    public static List<DiscoveryItem> WastedExtensions(FileSystemNode root)
    {
        var bytes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        void Walk(FileSystemNode dir)
        {
            foreach (var child in dir.Children)
            {
                if (child.IsDirectory) { Walk(child); continue; }
                var ext = Path.GetExtension(child.Name);
                if (ext.Length == 0 || !WastedExts.Contains(ext)) continue;
                bytes[ext] = bytes.GetValueOrDefault(ext) + child.Size;
                counts[ext] = counts.GetValueOrDefault(ext) + 1;
            }
        }

        Walk(root);
        return bytes
            .OrderByDescending(kv => kv.Value)
            .Select(kv => new DiscoveryItem(kv.Key, FormatCount(counts[kv.Key]), default, kv.Value, null))
            .ToList();
    }

    /// <summary>Pastas de artefato de desenvolvimento (a raiz de cada, sem subsumir).</summary>
    public static List<DiscoveryItem> DevJunk(FileSystemNode root)
    {
        var found = new List<FileSystemNode>();

        void Walk(FileSystemNode dir)
        {
            foreach (var child in dir.Children)
            {
                if (!child.IsDirectory) continue;
                if (DevJunkDirs.Contains(child.Name))
                    found.Add(child); // não desce: um node_modules aninhado já conta no de cima
                else
                    Walk(child);
            }
        }

        Walk(root);
        return found
            .OrderByDescending(n => n.Size)
            .Take(MaxResults)
            .Select(n => new DiscoveryItem(n.Name, Path.GetDirectoryName(n.FullPath) ?? "", n.LastWriteUtc, n.Size, n))
            .ToList();
    }

    /// <summary>Pastas sem nenhum arquivo na subárvore (a raiz vazia de cada, sem subsumir).</summary>
    public static List<DiscoveryItem> EmptyFolders(FileSystemNode root)
    {
        var found = new List<FileSystemNode>();

        void Walk(FileSystemNode dir)
        {
            foreach (var child in dir.Children)
            {
                if (!child.IsDirectory) continue;
                if (child.Size == 0)
                    found.Add(child); // vazia: nenhum arquivo aqui dentro — não desce
                else
                    Walk(child);
            }
        }

        Walk(root);
        return found
            .OrderByDescending(n => n.LastWriteUtc)
            .Take(MaxResults)
            .Select(n => new DiscoveryItem(n.Name, Path.GetDirectoryName(n.FullPath) ?? "", n.LastWriteUtc, 0, n))
            .ToList();
    }

    /// <summary>Arquivos grandes não acessados há muito tempo.</summary>
    public static List<DiscoveryItem> LargeFilesByAge(FileSystemNode root, DateTime accessCutoffUtc, long minBytes)
    {
        var found = new List<FileSystemNode>();

        void Walk(FileSystemNode dir)
        {
            foreach (var child in dir.Children)
            {
                if (child.IsDirectory) { Walk(child); continue; }
                if (child.Size >= minBytes && child.LastAccessUtc != default && child.LastAccessUtc < accessCutoffUtc)
                    found.Add(child);
            }
        }

        Walk(root);
        return found
            .OrderByDescending(n => n.Size)
            .Take(MaxResults)
            .Select(n => new DiscoveryItem(n.Name, Path.GetDirectoryName(n.FullPath) ?? "", n.LastAccessUtc, n.Size, n))
            .ToList();
    }

    private static string FormatCount(int count) => L.F("Dc.FileCount", count.ToString("N0"));
}
