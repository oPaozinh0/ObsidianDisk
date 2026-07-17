using System.IO;
using System.Windows.Media;
using ObsidianDisk.Models;

namespace ObsidianDisk.Services;

public enum FileCategory { Aplicativo, Video, Imagem, Audio, Documento, Compactado, Codigo, Outros }

public static class FileCategories
{
    public static readonly FileCategory[] All =
    {
        FileCategory.Aplicativo, FileCategory.Video, FileCategory.Imagem, FileCategory.Audio,
        FileCategory.Documento, FileCategory.Compactado, FileCategory.Codigo, FileCategory.Outros,
    };

    private static readonly Dictionary<string, FileCategory> ExtMap = BuildExtMap();

    private static Dictionary<string, FileCategory> BuildExtMap()
    {
        var map = new Dictionary<string, FileCategory>(StringComparer.OrdinalIgnoreCase);
        void Add(FileCategory cat, params string[] exts) { foreach (var e in exts) map[e] = cat; }

        Add(FileCategory.Video, ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".flv", ".m4v", ".ts", ".mpg", ".mpeg");
        Add(FileCategory.Audio, ".mp3", ".wav", ".flac", ".ogg", ".m4a", ".aac", ".wma", ".opus", ".mid");
        Add(FileCategory.Imagem, ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg", ".ico", ".tiff", ".raw", ".psd", ".heic");
        Add(FileCategory.Compactado, ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".iso", ".cab", ".vhd", ".vhdx", ".wim");
        Add(FileCategory.Documento, ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".md", ".odt", ".epub", ".csv", ".rtf");
        Add(FileCategory.Codigo, ".cs", ".js", ".ts", ".py", ".java", ".cpp", ".c", ".h", ".html", ".css", ".json", ".xml", ".yml", ".yaml", ".sql", ".go", ".rs", ".php", ".ps1", ".sh");
        Add(FileCategory.Aplicativo, ".exe", ".dll", ".msi", ".sys", ".bin", ".dat", ".pak", ".so", ".apk", ".appx", ".msix", ".jar");

        return map;
    }

    public static FileCategory Classify(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return ext.Length > 0 && ExtMap.TryGetValue(ext, out var cat) ? cat : FileCategory.Outros;
    }

    public static string LabelOf(FileCategory cat) => cat switch
    {
        FileCategory.Aplicativo => L.T("Cat.App"),
        FileCategory.Video => L.T("Cat.Video"),
        FileCategory.Imagem => L.T("Cat.Image"),
        FileCategory.Audio => L.T("Cat.Audio"),
        FileCategory.Documento => L.T("Cat.Doc"),
        FileCategory.Compactado => L.T("Cat.Zip"),
        FileCategory.Codigo => L.T("Cat.Code"),
        _ => L.T("Cat.Other"),
    };

    /// <summary>
    /// Quando ligado, usa a paleta Okabe-Ito — desenhada para permanecer
    /// distinguível em qualquer tipo de daltonismo. Definido na inicialização.
    /// </summary>
    public static bool ColorBlindSafe { get; set; }

    /// <summary>Cor vívida da categoria (cards, legenda).</summary>
    public static Color ColorOf(FileCategory cat) =>
        ColorBlindSafe ? OkabeItoOf(cat) : DefaultColorOf(cat);

    private static Color DefaultColorOf(FileCategory cat) => cat switch
    {
        FileCategory.Aplicativo => Color.FromRgb(0x3B, 0x82, 0xF6),
        FileCategory.Video => Color.FromRgb(0x8B, 0x5C, 0xF6),
        FileCategory.Imagem => Color.FromRgb(0x14, 0xB8, 0xA6),
        FileCategory.Audio => Color.FromRgb(0xF5, 0x9E, 0x0B),
        FileCategory.Documento => Color.FromRgb(0x22, 0xC5, 0x5E),
        FileCategory.Compactado => Color.FromRgb(0xEF, 0x44, 0x44),
        FileCategory.Codigo => Color.FromRgb(0x06, 0xB6, 0xD4),
        _ => Color.FromRgb(0x64, 0x74, 0x8B),
    };

    /// <summary>Paleta Okabe-Ito: 8 cores, uma por categoria — nenhum par se confunde.</summary>
    private static Color OkabeItoOf(FileCategory cat) => cat switch
    {
        FileCategory.Aplicativo => Color.FromRgb(0x00, 0x72, 0xB2), // azul
        FileCategory.Video => Color.FromRgb(0xCC, 0x79, 0xA7),      // roxo-rosado
        FileCategory.Imagem => Color.FromRgb(0x56, 0xB4, 0xE9),     // azul-céu
        FileCategory.Audio => Color.FromRgb(0xE6, 0x9F, 0x00),      // laranja
        FileCategory.Documento => Color.FromRgb(0x00, 0x9E, 0x73),  // verde-azulado
        FileCategory.Compactado => Color.FromRgb(0xD5, 0x5E, 0x00), // vermelhão
        FileCategory.Codigo => Color.FromRgb(0xF0, 0xE4, 0x42),     // amarelo
        _ => Color.FromRgb(0x99, 0x99, 0x99),                       // cinza
    };

    /// <summary>Versão atenuada para os blocos do treemap (não estourar no fundo escuro).</summary>
    public static Color MapColorOf(FileCategory cat)
    {
        var c = ColorOf(cat);
        return Color.FromRgb((byte)(c.R * 0.68), (byte)(c.G * 0.68), (byte)(c.B * 0.68));
    }

    /// <summary>Soma o tamanho por categoria em toda a árvore (usar após o scan concluir).</summary>
    public static Dictionary<FileCategory, long> Aggregate(FileSystemNode root)
    {
        var totals = new Dictionary<FileCategory, long>();
        foreach (var cat in All) totals[cat] = 0;
        Walk(root);
        return totals;

        void Walk(FileSystemNode dir)
        {
            var children = dir.Children;
            int count = children.Count;
            for (int i = 0; i < count; i++)
            {
                var child = children[i];
                if (child.IsDirectory) Walk(child);
                else totals[Classify(child.Name)] += child.Size;
            }
        }
    }
}
