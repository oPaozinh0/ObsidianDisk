using System.IO;
using ObsidianDisk.Models;

namespace ObsidianDisk.Tests;

/// <summary>Ajudantes para montar árvores <see cref="FileSystemNode"/> em memória nos testes.</summary>
internal static class NodeBuilder
{
    public static FileSystemNode File(string name, long size,
        DateTime? write = null, DateTime? access = null) =>
        new()
        {
            Name = name,
            FullPath = @"X\" + name,
            IsDirectory = false,
            Size = size,
            LastWriteUtc = write ?? default,
            LastAccessUtc = access ?? default,
        };

    public static FileSystemNode Dir(string fullPath, params FileSystemNode[] children)
    {
        var dir = new FileSystemNode
        {
            Name = Path.GetFileName(fullPath.TrimEnd('\\')),
            FullPath = fullPath,
            IsDirectory = true,
        };
        long total = 0;
        foreach (var child in children)
        {
            child.Parent = dir;
            dir.Children.Add(child);
            total += child.Size;
        }
        dir.Size = total;
        return dir;
    }
}
