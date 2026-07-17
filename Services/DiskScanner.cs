using System.IO;
using ObsidianDisk.Models;

namespace ObsidianDisk.Services;

public sealed record ScanProgress(long FilesScanned, long BytesScanned, string CurrentPath);

public sealed class DiskScanner
{
    private long _filesScanned;
    private long _bytesScanned;
    private string _currentPath = "";

    private static readonly EnumerationOptions EnumOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint, // evita loops de links simbólicos
    };

    public ScanProgress Progress =>
        new(Interlocked.Read(ref _filesScanned), Interlocked.Read(ref _bytesScanned), _currentPath);

    /// <summary>
    /// Inicia o scan e retorna a raiz imediatamente — a árvore é preenchida em segundo
    /// plano e os tamanhos se propagam ao vivo, permitindo renderização durante o scan.
    /// </summary>
    public (FileSystemNode Root, Task Task) StartScan(string rootPath, CancellationToken ct)
    {
        _filesScanned = 0;
        _bytesScanned = 0;

        var root = new FileSystemNode
        {
            Name = rootPath,
            FullPath = rootPath,
            IsDirectory = true,
        };

        var task = Task.Run(() => ScanDirectory(root, depth: 0, ct), ct);
        return (root, task);
    }

    private void ScanDirectory(FileSystemNode dirNode, int depth, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _currentPath = dirNode.FullPath;

        var subDirs = new List<FileSystemNode>();

        try
        {
            foreach (var entry in new DirectoryInfo(dirNode.FullPath).EnumerateFileSystemInfos("*", EnumOptions))
            {
                ct.ThrowIfCancellationRequested();

                if (entry is FileInfo file)
                {
                    var fileNode = new FileSystemNode
                    {
                        Name = file.Name,
                        FullPath = file.FullName,
                        IsDirectory = false,
                        Size = file.Length,
                        LastWriteUtc = file.LastWriteTimeUtc,
                        Parent = dirNode,
                    };
                    dirNode.Children.Add(fileNode);
                    dirNode.AddSizeUpwards(file.Length); // propaga ao vivo até a raiz
                    Interlocked.Increment(ref _filesScanned);
                    Interlocked.Add(ref _bytesScanned, file.Length);
                }
                else if (entry is DirectoryInfo dir)
                {
                    var childNode = new FileSystemNode
                    {
                        Name = dir.Name,
                        FullPath = dir.FullName,
                        IsDirectory = true,
                        Parent = dirNode,
                    };
                    dirNode.Children.Add(childNode);
                    subDirs.Add(childNode);
                }
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }

        // Paraleliza apenas nos níveis mais rasos, onde há mais ganho
        if (depth < 3 && subDirs.Count > 1)
        {
            Parallel.ForEach(subDirs, new ParallelOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = Environment.ProcessorCount,
            }, sub => ScanDirectory(sub, depth + 1, ct));
        }
        else
        {
            foreach (var sub in subDirs)
                ScanDirectory(sub, depth + 1, ct);
        }
    }
}
