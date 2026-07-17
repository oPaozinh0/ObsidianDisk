namespace ObsidianDisk.Models;

public sealed class FileSystemNode
{
    private long _size;

    public string Name { get; init; } = "";
    public string FullPath { get; init; } = "";
    public bool IsDirectory { get; init; }
    public DateTime LastWriteUtc { get; init; }  // arquivos e pastas
    public DateTime LastAccessUtc { get; init; } // arquivos e pastas — pode ser impreciso (ver DiskScanner)
    public DateTime CreationUtc { get; init; }   // arquivos e pastas
    public FileSystemNode? Parent { get; set; }
    public List<FileSystemNode> Children { get; } = new();

    /// <summary>Tamanho em bytes. Atualizado de forma atômica durante o scan (a UI lê ao vivo).</summary>
    public long Size
    {
        get => Interlocked.Read(ref _size);
        set => Interlocked.Exchange(ref _size, value);
    }

    /// <summary>Soma <paramref name="delta"/> a este nó e a todos os ancestrais.</summary>
    public void AddSizeUpwards(long delta)
    {
        for (var n = this; n is not null; n = n.Parent)
            Interlocked.Add(ref n._size, delta);
    }

    /// <summary>Ordena os filhos por tamanho (maior primeiro), recursivamente.</summary>
    public void SortBySizeDescending()
    {
        Children.Sort(static (a, b) => b.Size.CompareTo(a.Size));
        foreach (var child in Children)
            if (child.IsDirectory)
                child.SortBySizeDescending();
    }

    public static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : $"{value:0.##} {units[unit]}";
    }
}
