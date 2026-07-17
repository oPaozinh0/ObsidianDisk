using System.IO;
using System.Windows.Threading;
using ObsidianDisk.Models;

namespace ObsidianDisk.Services;

/// <summary>
/// Mantém a árvore escaneada em sincronia com o disco usando FileSystemWatcher.
/// Todas as mutações da árvore rodam na thread de UI (via Dispatcher), garantindo
/// que as páginas nunca leiam a árvore durante uma alteração.
/// </summary>
public sealed class LiveWatcher : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private FileSystemWatcher? _fsw;
    private FileSystemNode? _root;

    /// <summary>Disparado (na thread de UI) quando a árvore mudou.</summary>
    public event Action? TreeChanged;

    public bool IsRunning => _fsw is not null;

    public LiveWatcher(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public void Start(FileSystemNode root)
    {
        Stop();
        _root = root;
        try
        {
            _fsw = new FileSystemWatcher(root.FullPath)
            {
                IncludeSubdirectories = true,
                InternalBufferSize = 64 * 1024,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                               NotifyFilters.Size | NotifyFilters.LastWrite,
            };
            _fsw.Created += (_, e) => Post(() => OnCreated(e.FullPath));
            _fsw.Deleted += (_, e) => Post(() => OnDeleted(e.FullPath));
            _fsw.Changed += (_, e) => Post(() => OnChanged(e.FullPath));
            _fsw.Renamed += (_, e) => Post(() =>
            {
                OnDeleted(e.OldFullPath);
                OnCreated(e.FullPath);
            });
            _fsw.Error += (_, _) => { }; // estouro de buffer: o próximo rescan corrige
            _fsw.EnableRaisingEvents = true;
        }
        catch
        {
            _fsw?.Dispose();
            _fsw = null; // caminho removido/sem permissão — segue sem monitorar
        }
    }

    public void Stop()
    {
        _fsw?.Dispose();
        _fsw = null;
    }

    public void Dispose() => Stop();

    private void Post(Action action) =>
        _dispatcher.BeginInvoke(action, DispatcherPriority.Background);

    // ---------------- Mutações (sempre na thread de UI) ----------------

    private void OnCreated(string path)
    {
        if (_root is null || FindNode(path) is not null) return;

        var parent = FindNode(Path.GetDirectoryName(path) ?? "");
        if (parent is null || !parent.IsDirectory) return; // ancestral fora da árvore mapeada

        try
        {
            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                var node = new FileSystemNode
                {
                    Name = info.Name,
                    FullPath = info.FullName,
                    IsDirectory = false,
                    Size = info.Length,
                    LastWriteUtc = info.LastWriteTimeUtc,
                    Parent = parent,
                };
                parent.Children.Add(node);
                parent.AddSizeUpwards(info.Length);
            }
            else if (Directory.Exists(path))
            {
                var node = new FileSystemNode
                {
                    Name = Path.GetFileName(path),
                    FullPath = path,
                    IsDirectory = true,
                    Parent = parent,
                };
                parent.Children.Add(node);
                // Uma pasta movida para dentro pode ter conteúdo: escaneia o subtree
                // em background (o nó ainda é raso; anexar os filhos ocorre na UI depois)
                _ = ScanSubtreeAsync(node);
            }
            else
            {
                return;
            }
            TreeChanged?.Invoke();
        }
        catch { }
    }

    private async Task ScanSubtreeAsync(FileSystemNode dirNode)
    {
        var scanner = new DiskScanner();
        var (detached, task) = scanner.StartScan(dirNode.FullPath, CancellationToken.None);
        try { await task; } catch { return; }

        // Anexa o resultado (na thread de UI, pois ScanSubtreeAsync foi iniciado nela)
        if (dirNode.Parent is null) return; // pasta já removida enquanto escaneava
        foreach (var child in detached.Children)
        {
            child.Parent = dirNode;
            dirNode.Children.Add(child);
        }
        dirNode.AddSizeUpwards(detached.Size);
        TreeChanged?.Invoke();
    }

    private void OnDeleted(string path)
    {
        var node = FindNode(path);
        if (node?.Parent is not { } parent) return;

        parent.Children.Remove(node);
        parent.AddSizeUpwards(-node.Size);
        node.Parent = null;
        TreeChanged?.Invoke();
    }

    private void OnChanged(string path)
    {
        var node = FindNode(path);
        if (node is null || node.IsDirectory) return;

        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return;
            long delta = info.Length - node.Size;
            if (delta == 0) return;

            node.Size = info.Length;
            node.Parent?.AddSizeUpwards(delta);
            TreeChanged?.Invoke();
        }
        catch { }
    }

    private FileSystemNode? FindNode(string fullPath)
    {
        if (_root is null || string.IsNullOrEmpty(fullPath)) return null;

        string rootPath = _root.FullPath.TrimEnd('\\');
        if (!fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase)) return null;
        if (fullPath.Length <= rootPath.Length + 1) return _root;

        var segments = fullPath[(rootPath.Length + 1)..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var current = _root;
        foreach (var segment in segments)
        {
            current = current.Children.FirstOrDefault(c =>
                c.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));
            if (current is null) return null;
        }
        return current;
    }
}
