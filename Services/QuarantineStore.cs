using System.IO;
using System.Text.Json;

namespace ObsidianDisk.Services;

/// <summary>Um item em quarentena: metadados + de onde veio.</summary>
public sealed record QuarantineItem(
    string Id, string OriginalPath, string Name, DateTime DeletedUtc, long Size, bool IsDirectory);

/// <summary>
/// Lixeira interna com retenção: "excluir" move o item para uma pasta gerenciada em
/// %LocalAppData%\ObsidianDisk\quarantine em vez da Lixeira do Windows. Cada item guarda de
/// onde veio (meta.json) e pode ser restaurado. Itens além da retenção são apagados de vez.
/// </summary>
public static class QuarantineStore
{
    public const int RetentionDays = 30;

    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ObsidianDisk", "quarantine");

    private sealed record Meta(string OriginalPath, string Name, DateTime DeletedUtc, long Size, bool IsDirectory);

    /// <summary>Move um arquivo/pasta para a quarentena. Retorna true em caso de sucesso.</summary>
    public static bool Quarantine(string sourcePath, long size)
    {
        try
        {
            string name = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(name)) return false;
            bool isDir = Directory.Exists(sourcePath);
            if (!isDir && !File.Exists(sourcePath)) return false;

            string id = Guid.NewGuid().ToString("N");
            string itemDir = Path.Combine(Root, id);
            Directory.CreateDirectory(itemDir);

            // Reaproveita o mover entre volumes (a quarentena pode estar noutro drive que a origem)
            if (!FileDeletion.Move(sourcePath, itemDir))
            {
                TryDeleteDir(itemDir);
                return false;
            }

            var meta = new Meta(sourcePath, name, DateTime.UtcNow, size, isDir);
            File.WriteAllText(Path.Combine(itemDir, "meta.json"), JsonSerializer.Serialize(meta));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Itens atualmente em quarentena, do mais recente ao mais antigo.</summary>
    public static List<QuarantineItem> List()
    {
        var result = new List<QuarantineItem>();
        try
        {
            if (!Directory.Exists(Root)) return result;
            foreach (var dir in Directory.GetDirectories(Root))
            {
                var meta = ReadMeta(dir);
                if (meta is null) continue;
                result.Add(new QuarantineItem(Path.GetFileName(dir), meta.OriginalPath, meta.Name,
                    meta.DeletedUtc, meta.Size, meta.IsDirectory));
            }
        }
        catch { }
        return result.OrderByDescending(i => i.DeletedUtc).ToList();
    }

    /// <summary>Devolve o item ao caminho original. Não sobrescreve se já existir algo lá.</summary>
    public static bool Restore(string id)
    {
        try
        {
            string itemDir = Path.Combine(Root, id);
            var meta = ReadMeta(itemDir);
            if (meta is null) return false;

            string payload = Path.Combine(itemDir, meta.Name);
            string? originalParent = Path.GetDirectoryName(meta.OriginalPath);
            if (string.IsNullOrEmpty(originalParent)) return false;

            if (!FileDeletion.Move(payload, originalParent)) return false;
            TryDeleteDir(itemDir);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Restaura o item mais recente cuja origem era <paramref name="originalPath"/> (usado no Desfazer).</summary>
    public static bool RestoreByOriginalPath(string originalPath)
    {
        var match = List().FirstOrDefault(i =>
            string.Equals(i.OriginalPath, originalPath, StringComparison.OrdinalIgnoreCase));
        return match is not null && Restore(match.Id);
    }

    /// <summary>Apaga o item de vez (sem restaurar).</summary>
    public static bool Purge(string id)
    {
        try
        {
            TryDeleteDir(Path.Combine(Root, id));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Apaga todos os itens além da retenção. Retorna quantos foram removidos.</summary>
    public static int PurgeExpired(int retentionDays = RetentionDays)
    {
        int removed = 0;
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        foreach (var item in List())
            if (item.DeletedUtc < cutoff && Purge(item.Id))
                removed++;
        return removed;
    }

    private static Meta? ReadMeta(string itemDir)
    {
        try
        {
            string file = Path.Combine(itemDir, "meta.json");
            return File.Exists(file) ? JsonSerializer.Deserialize<Meta>(File.ReadAllText(file)) : null;
        }
        catch
        {
            return null;
        }
    }

    private static void TryDeleteDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
    }
}
