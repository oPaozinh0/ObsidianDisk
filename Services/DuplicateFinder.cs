using System.IO;
using System.Security.Cryptography;
using ObsidianDisk.Models;

namespace ObsidianDisk.Services;

public sealed record DuplicateGroup(long FileSize, List<FileSystemNode> Files)
{
    public long WastedBytes => FileSize * (Files.Count - 1);
}

public sealed record DuplicateProgress(int Done, int Total, string Phase);

/// <summary>
/// Encontra arquivos duplicados em três estágios: agrupa por tamanho,
/// depois por hash parcial (256 KB) e confirma com SHA-256 completo.
/// </summary>
public static class DuplicateFinder
{
    private const int PartialBytes = 256 * 1024;

    public static async Task<List<DuplicateGroup>> FindAsync(
        FileSystemNode root, long minSize, IProgress<DuplicateProgress> progress, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            // Estágio 1: agrupar por tamanho
            var bySize = new Dictionary<long, List<FileSystemNode>>();
            Collect(root);

            void Collect(FileSystemNode dir)
            {
                foreach (var child in dir.Children)
                {
                    if (child.IsDirectory) Collect(child);
                    else if (child.Size >= minSize)
                    {
                        if (!bySize.TryGetValue(child.Size, out var list))
                            bySize[child.Size] = list = new List<FileSystemNode>();
                        list.Add(child);
                    }
                }
            }

            var candidates = bySize.Values.Where(l => l.Count > 1).ToList();
            int totalFiles = candidates.Sum(l => l.Count);
            int done = 0;

            // Estágio 2: hash parcial; Estágio 3: hash completo
            var groups = new List<DuplicateGroup>();
            var options = new ParallelOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2),
            };

            var confirmed = new List<(long Size, List<FileSystemNode> Files)>();
            var gate = new object();

            Parallel.ForEach(candidates, options, sameSize =>
            {
                var byPartial = HashGroups(sameSize, full: false);
                foreach (var partialGroup in byPartial.Where(g => g.Count > 1))
                {
                    // arquivos pequenos: o hash parcial JÁ cobre o conteúdo todo
                    var finalGroups = partialGroup[0].Size <= PartialBytes
                        ? new List<List<FileSystemNode>> { partialGroup }
                        : HashGroups(partialGroup, full: true).Where(g => g.Count > 1).ToList();

                    foreach (var g in finalGroups.Where(g => g.Count > 1))
                        lock (gate)
                            confirmed.Add((g[0].Size, g));
                }
            });

            List<List<FileSystemNode>> HashGroups(List<FileSystemNode> files, bool full)
            {
                var map = new Dictionary<string, List<FileSystemNode>>();
                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();
                    string? hash = ComputeHash(file.FullPath, full);
                    if (!full)
                    {
                        int d = Interlocked.Increment(ref done);
                        if (d % 20 == 0 || d == totalFiles)
                            progress.Report(new DuplicateProgress(d, totalFiles,
                                full ? "Confirmando duplicados" : "Comparando arquivos"));
                    }
                    if (hash is null) continue;
                    if (!map.TryGetValue(hash, out var list))
                        map[hash] = list = new List<FileSystemNode>();
                    list.Add(file);
                }
                return map.Values.ToList();
            }

            return confirmed
                .Select(c => new DuplicateGroup(c.Size, c.Files))
                .OrderByDescending(g => g.WastedBytes)
                .ToList();
        }, ct);
    }

    private static string? ComputeHash(string path, bool full)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                bufferSize: 1 << 16, FileOptions.SequentialScan);
            using var sha = SHA256.Create();

            if (full)
                return Convert.ToHexString(sha.ComputeHash(stream));

            var buffer = new byte[PartialBytes];
            int read = stream.Read(buffer, 0, buffer.Length);
            return Convert.ToHexString(sha.ComputeHash(buffer, 0, read));
        }
        catch
        {
            return null; // em uso / sem permissão — ignora
        }
    }
}
