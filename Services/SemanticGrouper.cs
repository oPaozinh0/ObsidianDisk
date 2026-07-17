using System.Windows.Media;
using ObsidianDisk.Models;

namespace ObsidianDisk.Services;

/// <summary>Bucket semântico exibido na Visão Geral (Programas, Jogos, Windows…).</summary>
public sealed record SpaceBucket(string Label, string Glyph, Color Color, long Size, FileSystemNode? PrimaryNode);

/// <summary>
/// Agrupa a árvore escaneada em blocos semânticos no estilo "gerenciador de disco":
/// para a raiz de uma unidade usa heurísticas de pastas conhecidas do Windows;
/// para pastas comuns usa as maiores subpastas.
/// </summary>
public static class SemanticGrouper
{
    // Glifos Segoe MDL2
    private const string GlyphApps = "";
    private const string GlyphGame = "";
    private const string GlyphWindows = "";
    private const string GlyphUsers = "";
    private const string GlyphTemp = "";
    private const string GlyphFolder = "";

    private static readonly Color Blue = Color.FromRgb(0x3B, 0x82, 0xF6);
    private static readonly Color Purple = Color.FromRgb(0x8B, 0x5C, 0xF6);
    private static readonly Color Teal = Color.FromRgb(0x14, 0x9E, 0x8C);
    private static readonly Color Orange = Color.FromRgb(0xE0, 0x76, 0x2F);
    private static readonly Color Yellow = Color.FromRgb(0xD9, 0xA5, 0x21);
    private static readonly Color Red = Color.FromRgb(0xDC, 0x4A, 0x4A);
    private static readonly Color Slate = Color.FromRgb(0x64, 0x74, 0x8B);

    private static readonly Color[] FolderPalette = { Blue, Purple, Teal, Orange, Yellow, Red };

    public static List<SpaceBucket> Group(FileSystemNode root)
    {
        bool isDriveRoot = root.FullPath.TrimEnd('\\').Length == 2; // "C:"
        var buckets = isDriveRoot ? GroupDriveRoot(root) : GroupFolder(root);
        return buckets.Where(b => b.Size > 0).OrderByDescending(b => b.Size).ToList();
    }

    // ---------------- Raiz de unidade: heurísticas do Windows ----------------

    private static List<SpaceBucket> GroupDriveRoot(FileSystemNode root)
    {
        static FileSystemNode? Child(FileSystemNode? dir, string name) =>
            dir?.Children.FirstOrDefault(c =>
                c.IsDirectory && c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        var pf = Child(root, "Program Files");
        var pf86 = Child(root, "Program Files (x86)");
        var programData = Child(root, "ProgramData");
        var windows = Child(root, "Windows");
        var users = Child(root, "Users") ?? Child(root, "Usuários");

        // ---- Jogos: pastas conhecidas na raiz e dentro de Program Files ----
        string[] topGameNames = { "Games", "Jogos", "SteamLibrary", "XboxGames", "Epic Games", "Riot Games", "GOG Games" };
        string[] pfGameNames = { "Steam", "Epic Games", "Riot Games", "GOG Galaxy", "Electronic Arts", "EA Games", "Ubisoft", "Rockstar Games", "Battle.net" };

        var gameNodes = new List<FileSystemNode>();
        foreach (var name in topGameNames)
            if (Child(root, name) is { } g) gameNodes.Add(g);

        long gamesInsidePrograms = 0;
        foreach (var parent in new[] { pf, pf86 })
            foreach (var name in pfGameNames)
                if (Child(parent, name) is { } g)
                {
                    gameNodes.Add(g);
                    gamesInsidePrograms += g.Size;
                }

        long games = gameNodes.Sum(n => n.Size);

        // ---- Arquivos Temp: Windows\Temp, Temp dos usuários, Lixeira ----
        long temp = 0;
        var winTemp = Child(windows, "Temp");
        temp += winTemp?.Size ?? 0;
        if (users is not null)
            foreach (var user in users.Children.Where(c => c.IsDirectory))
                temp += Child(Child(Child(user, "AppData"), "Local"), "Temp")?.Size ?? 0;
        var recycleBin = Child(root, "$Recycle.Bin");
        temp += recycleBin?.Size ?? 0;

        // ---- Totais com sobreposições descontadas ----
        long programas = (pf?.Size ?? 0) + (pf86?.Size ?? 0) + (programData?.Size ?? 0) - gamesInsidePrograms;
        long windowsSize = (windows?.Size ?? 0) - (winTemp?.Size ?? 0);
        long usersSize = users?.Size ?? 0;
        if (users is not null)
            foreach (var user in users.Children.Where(c => c.IsDirectory))
                usersSize -= Child(Child(Child(user, "AppData"), "Local"), "Temp")?.Size ?? 0;

        long known = programas + games + windowsSize + usersSize + temp;
        long outros = Math.Max(0, root.Size - known);

        return new List<SpaceBucket>
        {
            new(L.T("Bucket.Programs"), GlyphApps, Blue, programas, Biggest(pf, pf86, programData)),
            new(L.T("Bucket.Games"), GlyphGame, Purple, games, gameNodes.OrderByDescending(n => n.Size).FirstOrDefault()),
            new(L.T("Bucket.Windows"), GlyphWindows, Teal, windowsSize, windows),
            new(L.T("Bucket.Users"), GlyphUsers, Orange, usersSize, users),
            new(L.T("Bucket.Temp"), GlyphTemp, Yellow, temp, Biggest(winTemp, recycleBin)),
            new(L.T("Cat.Other"), GlyphFolder, Red, outros, null),
        };

        static FileSystemNode? Biggest(params FileSystemNode?[] nodes) =>
            nodes.Where(n => n is not null).OrderByDescending(n => n!.Size).FirstOrDefault();
    }

    // ---------------- Pasta comum: maiores subpastas ----------------

    private static List<SpaceBucket> GroupFolder(FileSystemNode root)
    {
        var dirs = root.Children.Where(c => c.IsDirectory && c.Size > 0)
                       .OrderByDescending(c => c.Size).ToList();

        var buckets = new List<SpaceBucket>();
        int i = 0;
        foreach (var dir in dirs.Take(5))
        {
            buckets.Add(new SpaceBucket(dir.Name, GlyphFolder, FolderPalette[i % FolderPalette.Length], dir.Size, dir));
            i++;
        }

        long rest = root.Size - buckets.Sum(b => b.Size);
        if (rest > 0)
            buckets.Add(new SpaceBucket(L.T("Cat.Other"), GlyphFolder, Slate, rest, null));

        return buckets;
    }
}
