using System.IO;
using System.Runtime.InteropServices;

namespace ObsidianDisk.Services;

public sealed record TempTarget(
    string Key, string Label, string Description, string[] Paths,
    bool DefaultChecked, bool IsRecycleBin = false);

public sealed record CleanResult(long FreedBytes, int FailedItems);

/// <summary>Mede e limpa os locais de arquivos temporários conhecidos do Windows.</summary>
public static class TempCleaner
{
    public static List<TempTarget> GetTargets()
    {
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        return new List<TempTarget>
        {
            new("user-temp", "Temp do usuário",
                "Arquivos temporários de aplicativos (%TEMP%). Seguro limpar.",
                new[] { Path.Combine(localAppData, "Temp") }, DefaultChecked: true),

            new("win-temp", "Temp do Windows",
                @"C:\Windows\Temp. Seguro limpar; itens em uso são pulados.",
                new[] { Path.Combine(windows, "Temp") }, DefaultChecked: true),

            new("wu-cache", "Cache do Windows Update",
                "Instaladores já baixados (SoftwareDistribution\\Download). O Windows baixa de novo se precisar.",
                new[] { Path.Combine(windows, "SoftwareDistribution", "Download") }, DefaultChecked: false),

            new("thumbs", "Cache de miniaturas",
                "Miniaturas do Explorer (thumbcache). São recriadas conforme você navega.",
                new[] { Path.Combine(localAppData, "Microsoft", "Windows", "Explorer") }, DefaultChecked: false),

            new("wer", "Relatórios de erro do Windows",
                "Relatórios de travamentos já enviados/enfileirados (WER).",
                new[]
                {
                    Path.Combine(programData, "Microsoft", "Windows", "WER", "ReportQueue"),
                    Path.Combine(programData, "Microsoft", "Windows", "WER", "ReportArchive"),
                }, DefaultChecked: false),

            new("recycle", "Lixeira (todas as unidades)",
                "Esvazia a Lixeira. Os itens não poderão mais ser restaurados.",
                Array.Empty<string>(), DefaultChecked: false, IsRecycleBin: true),
        };
    }

    // ---------------- Medição ----------------

    public static long Measure(TempTarget target)
    {
        if (target.IsRecycleBin)
            return QueryRecycleBinSize();

        long total = 0;
        foreach (var path in target.Paths)
            total += MeasureDirectory(path, target.Key == "thumbs");
        return total;
    }

    private static long MeasureDirectory(string path, bool thumbsOnly)
    {
        try
        {
            var dir = new DirectoryInfo(path);
            if (!dir.Exists) return 0;

            var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = !thumbsOnly };
            long total = 0;
            foreach (var file in dir.EnumerateFiles(thumbsOnly ? "thumbcache_*" : "*", options))
            {
                try { total += file.Length; } catch { }
            }
            return total;
        }
        catch
        {
            return 0;
        }
    }

    // ---------------- Limpeza ----------------

    public static CleanResult Clean(TempTarget target, CancellationToken ct)
    {
        if (target.IsRecycleBin)
        {
            long size = QueryRecycleBinSize();
            const uint flags = SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND;
            int hr = SHEmptyRecycleBinW(IntPtr.Zero, null, flags);
            // S_OK ou lixeira já vazia contam como sucesso
            return hr == 0 ? new CleanResult(size, 0) : new CleanResult(0, 1);
        }

        long freed = 0;
        int failed = 0;
        foreach (var path in target.Paths)
        {
            var dir = new DirectoryInfo(path);
            if (!dir.Exists) continue;

            if (target.Key == "thumbs")
            {
                foreach (var file in SafeFiles(dir, "thumbcache_*"))
                    DeleteFile(file, ref freed, ref failed, ct);
            }
            else
            {
                CleanDirectoryContents(dir, ref freed, ref failed, ct);
            }
        }
        return new CleanResult(freed, failed);
    }

    /// <summary>Apaga o conteúdo (não a pasta em si), arquivo por arquivo, pulando o que estiver em uso.</summary>
    private static void CleanDirectoryContents(DirectoryInfo dir, ref long freed, ref int failed, CancellationToken ct)
    {
        foreach (var file in SafeFiles(dir, "*"))
            DeleteFile(file, ref freed, ref failed, ct);

        foreach (var sub in SafeDirs(dir))
        {
            ct.ThrowIfCancellationRequested();
            CleanDirectoryContents(sub, ref freed, ref failed, ct);
            try { sub.Delete(); } catch { } // só remove se ficou vazia
        }
    }

    private static void DeleteFile(FileInfo file, ref long freed, ref int failed, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            long size = file.Length;
            if (file.IsReadOnly) file.IsReadOnly = false;
            file.Delete();
            freed += size;
        }
        catch
        {
            failed++;
        }
    }

    private static IEnumerable<FileInfo> SafeFiles(DirectoryInfo dir, string pattern)
    {
        try { return dir.EnumerateFiles(pattern, new EnumerationOptions { IgnoreInaccessible = true }).ToList(); }
        catch { return Enumerable.Empty<FileInfo>(); }
    }

    private static IEnumerable<DirectoryInfo> SafeDirs(DirectoryInfo dir)
    {
        try { return dir.EnumerateDirectories("*", new EnumerationOptions { IgnoreInaccessible = true }).ToList(); }
        catch { return Enumerable.Empty<DirectoryInfo>(); }
    }

    // ---------------- Lixeira (shell32) ----------------

    private const uint SHERB_NOCONFIRMATION = 0x1;
    private const uint SHERB_NOPROGRESSUI = 0x2;
    private const uint SHERB_NOSOUND = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO
    {
        public uint cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBinW(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBinW(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    private static long QueryRecycleBinSize()
    {
        var info = new SHQUERYRBINFO { cbSize = (uint)Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBinW(null, ref info) == 0 ? info.i64Size : 0;
    }
}
