using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ObsidianDisk.Models;
using ObsidianDisk.Services;

namespace ObsidianDisk.Poc;

/// <summary>Janela mínima do PoC: escolher uma pasta, escanear e ver o treemap renderizado por Skia.</summary>
public sealed class MainWindow : Window
{
    private readonly TreemapControl _treemap = new();
    private readonly TextBlock _status = new()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x93, 0xA7)),
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(4, 0, 12, 0),
    };
    private readonly DiskScanner _scanner = new();
    private bool _scanning;

    public MainWindow()
    {
        Title = "ObsidianDisk — PoC Avalonia (Skia)";
        Width = 1100;
        Height = 760;
        Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x11, 0x17));

        var scanButton = new Button
        {
            Content = "Escanear pasta…",
            Margin = new Thickness(12, 10, 0, 10),
            Padding = new Thickness(14, 6),
        };
        scanButton.Click += async (_, _) => await ScanAsync();

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal };
        toolbar.Children.Add(scanButton);
        toolbar.Children.Add(_status);

        var root = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        root.Children.Add(_treemap); // preenche o resto

        Content = root;
        _status.Text = "Escolha uma pasta. Render: Skia (GPU) — compare o nitidez dos glifos com o app WPF.";
    }

    private async Task ScanAsync()
    {
        if (_scanning) return;

        var folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { AllowMultiple = false });
        if (folders.Count == 0) return;

        var path = folders[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        _scanning = true;
        var (node, task) = _scanner.StartScan(path, CancellationToken.None);
        _treemap.Root = node;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += (_, _) =>
        {
            _treemap.InvalidateVisual();
            var p = _scanner.Progress;
            _status.Text = $"Escaneando… {p.FilesScanned:N0} arquivos · {FileSystemNode.FormatSize(p.BytesScanned)}";
        };
        timer.Start();

        await task;
        timer.Stop();
        node.SortBySizeDescending();
        _treemap.InvalidateVisual();

        var done = _scanner.Progress;
        _status.Text = $"Pronto · {done.FilesScanned:N0} arquivos · {FileSystemNode.FormatSize(node.Size)}";
        _scanning = false;
    }
}
