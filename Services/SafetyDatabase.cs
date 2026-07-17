using System.IO;
using System.Windows.Media;
using ObsidianDisk.Models;

namespace ObsidianDisk.Services;

public enum SafetyLevel { Unknown, Safe, Caution, Never }

/// <summary>Categoria do item conhecido — define o texto padrão e o nível.</summary>
public enum SafetyKind
{
    SystemCore, SystemCache, Temp, RecycleBin, UserData, InstalledApp,
    GameData, DevCache, DevSource, BrowserCache, Recovery, SystemFile,
}

public sealed record SafetyInfo(SafetyLevel Level, string Description, string? Advice);

/// <summary>
/// Diz, em linguagem simples, o que é um arquivo/pasta conhecido do Windows e
/// se dá para apagar. É o que separa "isso é grande" de "isso pode sair".
/// </summary>
public static class SafetyDatabase
{
    private sealed record Entry(SafetyKind Kind, string? SpecialKey = null);

    // ---- Nível padrão por categoria ----
    private static SafetyLevel LevelOf(SafetyKind kind) => kind switch
    {
        SafetyKind.SystemCache or SafetyKind.Temp or SafetyKind.RecycleBin
            or SafetyKind.DevCache or SafetyKind.BrowserCache => SafetyLevel.Safe,
        SafetyKind.SystemCore or SafetyKind.DevSource or SafetyKind.Recovery => SafetyLevel.Never,
        _ => SafetyLevel.Caution,
    };

    private static string? AdviceKeyOf(SafetyKind kind) => kind switch
    {
        SafetyKind.SystemCore => "Safety.A.SystemCore",
        SafetyKind.InstalledApp => "Safety.A.InstalledApp",
        SafetyKind.GameData => "Safety.A.GameData",
        SafetyKind.Recovery => "Safety.A.Recovery",
        SafetyKind.UserData => "Safety.A.UserData",
        _ => null,
    };

    // ---- Tabelas de correspondência (montadas uma vez) ----

    /// <summary>Caminhos absolutos exatos (pastas especiais expandidas).</summary>
    private static readonly Dictionary<string, Entry> ExactPaths = BuildExactPaths();

    /// <summary>Sufixos de caminho, ex.: "\Windows\WinSxS".</summary>
    private static readonly (string Suffix, Entry Entry)[] PathSuffixes =
    {
        (@"\Windows\WinSxS", new(SafetyKind.SystemCore, "Safety.S.WinSxS")),
        (@"\Windows\System32", new(SafetyKind.SystemCore, "Safety.S.System32")),
        (@"\Windows\SysWOW64", new(SafetyKind.SystemCore)),
        (@"\Windows\Installer", new(SafetyKind.SystemCore, "Safety.S.Installer")),
        (@"\Windows\assembly", new(SafetyKind.SystemCore)),
        (@"\Windows\Boot", new(SafetyKind.SystemCore)),
        (@"\Windows\Fonts", new(SafetyKind.SystemCore)),
        (@"\Windows\Temp", new(SafetyKind.Temp)),
        (@"\Windows\Logs", new(SafetyKind.SystemCache)),
        (@"\Windows\Minidump", new(SafetyKind.SystemCache)),
        (@"\Windows\Prefetch", new(SafetyKind.SystemCache)),
        (@"\Windows\Panther", new(SafetyKind.SystemCache)),
        (@"\Windows\SoftwareDistribution\Download", new(SafetyKind.SystemCache)),
        (@"\Program Files\WindowsApps", new(SafetyKind.SystemCore)),
        (@"\AppData\Local\Temp", new(SafetyKind.Temp)),
        (@"\AppData\Local\CrashDumps", new(SafetyKind.SystemCache)),
        (@"\AppData\Local\Microsoft\Windows\Explorer", new(SafetyKind.SystemCache)),
        (@"\AppData\Local\Microsoft\Windows\INetCache", new(SafetyKind.BrowserCache)),
        (@"\AppData\Local\Packages", new(SafetyKind.InstalledApp)),
        (@"\AppData\Local\pip\cache", new(SafetyKind.DevCache)),
        (@"\AppData\Local\npm-cache", new(SafetyKind.DevCache)),
        (@"\AppData\Roaming\npm-cache", new(SafetyKind.DevCache)),
        (@"\AppData\Local\NuGet\Cache", new(SafetyKind.DevCache)),
        (@"\.nuget\packages", new(SafetyKind.DevCache)),
        (@"\.gradle\caches", new(SafetyKind.DevCache)),
        (@"\.m2\repository", new(SafetyKind.DevCache)),
        (@"\.cargo\registry", new(SafetyKind.DevCache)),
    };

    /// <summary>Nome de pasta em qualquer lugar da árvore.</summary>
    private static readonly Dictionary<string, Entry> FolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["node_modules"] = new(SafetyKind.DevCache, "Safety.S.NodeModules"),
        ["__pycache__"] = new(SafetyKind.DevCache),
        [".pytest_cache"] = new(SafetyKind.DevCache),
        [".mypy_cache"] = new(SafetyKind.DevCache),
        [".next"] = new(SafetyKind.DevCache),
        [".turbo"] = new(SafetyKind.DevCache),
        [".git"] = new(SafetyKind.DevSource, "Safety.S.Git"),
        [".svn"] = new(SafetyKind.DevSource),
        ["venv"] = new(SafetyKind.DevCache),
        [".venv"] = new(SafetyKind.DevCache),
        ["$Recycle.Bin"] = new(SafetyKind.RecycleBin),
        ["System Volume Information"] = new(SafetyKind.Recovery, "Safety.S.SVI"),
        ["Recovery"] = new(SafetyKind.Recovery),
        ["$WinREAgent"] = new(SafetyKind.SystemCache),
        ["Windows.old"] = new(SafetyKind.SystemCache, "Safety.S.WindowsOld"),
        ["steamapps"] = new(SafetyKind.GameData),
        ["SteamLibrary"] = new(SafetyKind.GameData),
        ["XboxGames"] = new(SafetyKind.GameData),
        ["EA Games"] = new(SafetyKind.GameData),
        ["Rockstar Games"] = new(SafetyKind.GameData),
        ["Riot Games"] = new(SafetyKind.GameData),
        ["Epic Games"] = new(SafetyKind.GameData),
        ["Cache"] = new(SafetyKind.BrowserCache),
        ["Code Cache"] = new(SafetyKind.BrowserCache),
        ["GPUCache"] = new(SafetyKind.BrowserCache),
        ["ShaderCache"] = new(SafetyKind.SystemCache),
        ["DXCache"] = new(SafetyKind.SystemCache),
    };

    /// <summary>Nome de arquivo (raiz do disco, normalmente).</summary>
    private static readonly Dictionary<string, Entry> FileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hiberfil.sys"] = new(SafetyKind.SystemFile, "Safety.S.Hiberfil"),
        ["pagefile.sys"] = new(SafetyKind.SystemFile, "Safety.S.Pagefile"),
        ["swapfile.sys"] = new(SafetyKind.SystemFile, "Safety.S.Pagefile"),
        ["bootmgr"] = new(SafetyKind.SystemCore),
        ["ntuser.dat"] = new(SafetyKind.SystemCore),
    };

    private static Dictionary<string, Entry> BuildExactPaths()
    {
        var map = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        void Add(Environment.SpecialFolder folder, SafetyKind kind)
        {
            var path = Environment.GetFolderPath(folder);
            if (!string.IsNullOrEmpty(path)) map[path.TrimEnd('\\')] = new Entry(kind);
        }

        Add(Environment.SpecialFolder.Windows, SafetyKind.SystemCore);
        Add(Environment.SpecialFolder.System, SafetyKind.SystemCore);
        Add(Environment.SpecialFolder.ProgramFiles, SafetyKind.InstalledApp);
        Add(Environment.SpecialFolder.ProgramFilesX86, SafetyKind.InstalledApp);
        Add(Environment.SpecialFolder.CommonApplicationData, SafetyKind.InstalledApp);
        Add(Environment.SpecialFolder.MyDocuments, SafetyKind.UserData);
        Add(Environment.SpecialFolder.MyPictures, SafetyKind.UserData);
        Add(Environment.SpecialFolder.MyVideos, SafetyKind.UserData);
        Add(Environment.SpecialFolder.MyMusic, SafetyKind.UserData);
        Add(Environment.SpecialFolder.Desktop, SafetyKind.UserData);
        Add(Environment.SpecialFolder.UserProfile, SafetyKind.UserData);

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile))
        {
            map[Path.Combine(profile, "Downloads")] = new Entry(SafetyKind.UserData);
            map[Path.Combine(profile, "OneDrive")] = new Entry(SafetyKind.UserData);
        }
        return map;
    }

    // ---- Consulta ----

    /// <summary>Retorna o veredito de segurança, ou null se o item for desconhecido.</summary>
    public static SafetyInfo? Lookup(FileSystemNode node)
    {
        string path = node.FullPath.TrimEnd('\\');

        if (ExactPaths.TryGetValue(path, out var entry))
            return Build(entry);

        if (!node.IsDirectory && FileNames.TryGetValue(node.Name, out entry))
            return Build(entry);

        foreach (var (suffix, e) in PathSuffixes)
            if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return Build(e);

        if (node.IsDirectory && FolderNames.TryGetValue(node.Name, out entry))
            return Build(entry);

        return null;
    }

    /// <summary>Como <see cref="Lookup"/>, mas a partir de um caminho puro (ex.: dados de snapshot).</summary>
    public static SafetyInfo? LookupPath(string fullPath, bool isDirectory)
    {
        string path = fullPath.TrimEnd('\\');

        if (ExactPaths.TryGetValue(path, out var entry))
            return Build(entry);

        foreach (var (suffix, e) in PathSuffixes)
            if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return Build(e);

        if (isDirectory)
        {
            var name = System.IO.Path.GetFileName(path);
            if (name.Length > 0 && FolderNames.TryGetValue(name, out entry))
                return Build(entry);
        }

        return null;
    }

    private static SafetyInfo Build(Entry entry)
    {
        string description = entry.SpecialKey is not null
            ? L.T(entry.SpecialKey)
            : L.T($"Safety.K.{entry.Kind}");

        string? advice = AdviceKeyOf(entry.Kind) is { } key ? L.T(key) : null;
        return new SafetyInfo(LevelOf(entry.Kind), description, advice);
    }

    // ---- Apresentação ----

    public static string LabelOf(SafetyLevel level) => level switch
    {
        SafetyLevel.Safe => L.T("Safety.Safe"),
        SafetyLevel.Caution => L.T("Safety.Caution"),
        SafetyLevel.Never => L.T("Safety.Never"),
        _ => "",
    };

    public static Color ColorOf(SafetyLevel level) => level switch
    {
        SafetyLevel.Safe => Color.FromRgb(0x22, 0xC5, 0x5E),
        SafetyLevel.Caution => Color.FromRgb(0xF5, 0x9E, 0x0B),
        SafetyLevel.Never => Color.FromRgb(0xEF, 0x44, 0x44),
        _ => Color.FromRgb(0x64, 0x74, 0x8B),
    };
}
