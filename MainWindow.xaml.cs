using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ObsidianDisk.Models;
using ObsidianDisk.Services;

namespace ObsidianDisk;

public partial class MainWindow : Window
{
    private readonly DiskScanner _scanner = new();
    private readonly DispatcherTimer _progressTimer;
    private AppSettings _settings;

    private FileSystemNode? _scanRoot;
    private FileSystemNode? _hoveredNode;
    private CancellationTokenSource? _cts;
    private string? _lastScanPath;
    private long _scanTargetBytes;

    public MainWindow()
    {
        InitializeComponent();

        _settings = AppStorage.LoadSettings();
        SettingsPage.Load(_settings);
        SettingsPage.SettingsChanged += s =>
        {
            _settings = s;
            LargeFilesPage.SetMinBytes(s.LargeFileMinBytes);
        };
        LargeFilesPage.SetMinBytes(_settings.LargeFileMinBytes);

        _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _progressTimer.Tick += (_, _) => UpdateProgressUi();

        OverviewPage.ScanRequested += StartOrCancelScan;
        OverviewPage.OpenInMapRequested += node =>
        {
            MapPage.SetViewRoot(node);
            NavMap.IsChecked = true;
        };

        MapPage.HoverChanged += OnTreemapHover;
        BuildTreemapContextMenu();

        LargeFilesPage.DeleteRequested += DeleteNodes;
        DuplicatesPage.DeleteRequested += DeleteNodes;

        PreviewKeyDown += OnKeyDown;
        StateChanged += (_, _) => OnWindowStateChanged();

        // "ObsidianDisk.exe <pasta>" já abre escaneando o caminho informado
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1 && Directory.Exists(args[1]))
        {
            OverviewPage.SetCustomFolder(args[1]);
            Loaded += (_, _) => StartOrCancelScan(args[1]);
        }
    }

    // ---------------- Janela (chrome personalizado) ----------------

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void OnWindowStateChanged()
    {
        // Com WindowChrome, a janela maximizada avança sobre as bordas da tela — compensa com margem
        bool max = WindowState == WindowState.Maximized;
        RootBorder.Margin = max ? new Thickness(7) : new Thickness(0);
        // Glifos Segoe MDL2: E923 = restaurar, E922 = maximizar
        MaximizeButton.Content = max ? ((char)0xE923).ToString() : ((char)0xE922).ToString();
    }

    // ---------------- Navegação ----------------

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (OverviewPage is null) return; // ainda inicializando

        OverviewPage.Visibility = ReferenceEquals(sender, NavOverview) ? Visibility.Visible : Visibility.Collapsed;
        MapPage.Visibility = ReferenceEquals(sender, NavMap) ? Visibility.Visible : Visibility.Collapsed;
        LargeFilesPage.Visibility = ReferenceEquals(sender, NavLargeFiles) ? Visibility.Visible : Visibility.Collapsed;
        DuplicatesPage.Visibility = ReferenceEquals(sender, NavDuplicates) ? Visibility.Visible : Visibility.Collapsed;
        CleanupPage.Visibility = ReferenceEquals(sender, NavCleanup) ? Visibility.Visible : Visibility.Collapsed;
        HistoryPage.Visibility = ReferenceEquals(sender, NavHistory) ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = ReferenceEquals(sender, NavSettings) ? Visibility.Visible : Visibility.Collapsed;

        if (ReferenceEquals(sender, NavHistory))
            HistoryPage.Reload();
        if (ReferenceEquals(sender, NavCleanup))
            CleanupPage.EnsureMeasured(); // mede os tamanhos na primeira visita
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Back && MapPage.Visibility == Visibility.Visible &&
            Keyboard.FocusedElement is not TextBox)
        {
            if (MapPage.GoUp())
                e.Handled = true;
        }
    }

    // ---------------- Escaneamento ----------------

    private void Rescan_Click(object sender, RoutedEventArgs e)
    {
        if (_lastScanPath is not null)
            StartOrCancelScan(_lastScanPath);
    }

    private async void StartOrCancelScan(string path)
    {
        if (_cts is not null)
        {
            _cts.Cancel();
            return;
        }

        if (!Directory.Exists(path))
        {
            StatusText.Text = "Caminho inválido.";
            return;
        }

        _cts = new CancellationTokenSource();
        _lastScanPath = path;
        OverviewPage.SetScanning(true);
        RescanButton.IsEnabled = false;
        ProgressPanel.Visibility = Visibility.Visible;

        _scanTargetBytes = 0;
        var drive = DriveInfo.GetDrives().FirstOrDefault(d =>
            d.IsReady && string.Equals(d.RootDirectory.FullName, path, StringComparison.OrdinalIgnoreCase));
        if (drive is not null)
            _scanTargetBytes = drive.TotalSize - drive.TotalFreeSpace;
        ScanProgressBar.IsIndeterminate = _scanTargetBytes == 0;
        ScanProgressBar.Value = 0;

        _progressTimer.Start();

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var (root, task) = _scanner.StartScan(path, _cts.Token);
            _scanRoot = root;
            MapPage.SetViewRoot(root);   // treemap ao vivo
            NavMap.IsChecked = true;     // acompanha o mapa se montando

            await task;
            stopwatch.Stop();

            root.SortBySizeDescending();

            var progress = _scanner.Progress;
            AppStorage.AppendHistory(new ScanRecord(DateTime.Now, path, root.Size, progress.FilesScanned));

            RefreshAllPages();
            LastScanText.Text = $"Última análise:\n{DateTime.Now:dd/MM/yyyy HH:mm}";
            StatusText.Text = $"Concluído em {stopwatch.Elapsed.TotalSeconds:0.#}s — " +
                              $"{progress.FilesScanned:N0} arquivos, {FileSystemNode.FormatSize(root.Size)} no total.";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Escaneamento cancelado — exibindo o que foi mapeado até aqui.";
            if (_scanRoot is not null && _scanRoot.Size > 0)
                RefreshAllPages();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Erro: {ex.Message}";
        }
        finally
        {
            _progressTimer.Stop();
            ProgressPanel.Visibility = Visibility.Collapsed;
            OverviewPage.SetScanning(false);
            RescanButton.IsEnabled = _lastScanPath is not null;
            _cts.Dispose();
            _cts = null;
        }
    }

    private void UpdateProgressUi()
    {
        var p = _scanner.Progress;
        ProgressText.Text = p.CurrentPath;

        if (_scanTargetBytes > 0)
        {
            double percent = Math.Min(100, p.BytesScanned * 100.0 / _scanTargetBytes);
            ScanProgressBar.Value = percent;
            ProgressStats.Text = $"{p.FilesScanned:N0} arquivos · {FileSystemNode.FormatSize(p.BytesScanned)} · {percent:0.#}%";
        }
        else
        {
            ProgressStats.Text = $"{p.FilesScanned:N0} arquivos · {FileSystemNode.FormatSize(p.BytesScanned)}";
        }

        MapPage.TreemapControl.InvalidateVisual(); // treemap ao vivo
    }

    private void RefreshAllPages()
    {
        if (_scanRoot is null) return;
        OverviewPage.UpdateFromScan(_scanRoot);
        LargeFilesPage.UpdateFromScan(_scanRoot);
        DuplicatesPage.UpdateFromScan(_scanRoot);
        MapPage.TreemapControl.InvalidateVisual();
        HistoryPage.Reload();
    }

    // ---------------- Hover / status ----------------

    private void OnTreemapHover(FileSystemNode? node)
    {
        _hoveredNode = node;
        var viewRoot = MapPage.ViewRoot;

        if (node is null)
        {
            if (viewRoot is not null)
                StatusText.Text = $"{viewRoot.FullPath} — {FileSystemNode.FormatSize(viewRoot.Size)}";
            return;
        }

        double percent = viewRoot is { Size: > 0 } ? node.Size * 100.0 / viewRoot.Size : 0;
        string kind = node.IsDirectory ? $"{node.Children.Count} itens" : "arquivo";
        StatusText.Text = $"{node.FullPath} — {FileSystemNode.FormatSize(node.Size)} ({percent:0.#}% da visão atual) · {kind}";
    }

    // ---------------- Exclusão ----------------

    private void BuildTreemapContextMenu()
    {
        var menu = new ContextMenu();
        MapPage.TreemapControl.ContextMenu = menu;

        // O alvo é capturado no momento em que o menu abre — o clique nos itens
        // não depende mais do hover (que pode mudar com o menu aberto).
        MapPage.TreemapControl.ContextMenuOpening += (_, e) =>
        {
            var target = _hoveredNode;
            if (target is null)
            {
                e.Handled = true; // nada sob o cursor: não abre o menu
                return;
            }
            RebuildTreemapContextMenu(menu, target);
        };
    }

    private void RebuildTreemapContextMenu(ContextMenu menu, FileSystemNode target)
    {
        menu.Items.Clear();
        AddContextSection(menu, target);

        // Clicando num arquivo, oferece também agir na PASTA que o contém —
        // é o que o usuário geralmente quer ao mirar numa pasta cheia de blocos.
        if (!target.IsDirectory && target.Parent is { } parent && !ReferenceEquals(parent, _scanRoot))
        {
            menu.Items.Add(new Separator());
            AddContextSection(menu, parent);
        }
    }

    private void AddContextSection(ContextMenu menu, FileSystemNode node)
    {
        string icon = node.IsDirectory ? "📁" : "📄";
        string details = node.IsDirectory
            ? $"{FileSystemNode.FormatSize(node.Size)} · {node.Children.Count} itens"
            : FileSystemNode.FormatSize(node.Size);

        // Cabeçalho: deixa explícito sobre o que as ações abaixo agem
        menu.Items.Add(new MenuItem
        {
            Header = $"{icon} {node.Name} — {details}",
            IsEnabled = false,
            FontWeight = FontWeights.Bold,
            // valor local vence o trigger de desabilitado — o cabeçalho fica em destaque
            Foreground = (System.Windows.Media.Brush)FindResource("Text"),
        });

        var openItem = new MenuItem { Header = "      Abrir no Explorer" };
        openItem.Click += (_, _) =>
        {
            string args = node.IsDirectory ? $"\"{node.FullPath}\"" : $"/select,\"{node.FullPath}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
        };

        var copyItem = new MenuItem { Header = "      Copiar caminho" };
        copyItem.Click += (_, _) => Clipboard.SetText(node.FullPath);

        var recycleItem = new MenuItem { Header = "      Excluir (Lixeira)" };
        recycleItem.Click += (_, _) => DeleteNodes(new[] { node }, permanent: false);

        var permanentItem = new MenuItem { Header = "      Excluir permanentemente…" };
        permanentItem.Click += (_, _) => DeleteNodes(new[] { node }, permanent: true);

        menu.Items.Add(openItem);
        menu.Items.Add(copyItem);
        menu.Items.Add(recycleItem);
        menu.Items.Add(permanentItem);
    }

    /// <summary>Conta arquivos recursivamente, com teto para não travar em pastas gigantes.</summary>
    private static string CountFiles(FileSystemNode dir)
    {
        const int cap = 100_000;
        int count = 0;
        Walk(dir);
        return count >= cap ? $"{cap:N0}+" : count.ToString("N0");

        void Walk(FileSystemNode d)
        {
            foreach (var child in d.Children)
            {
                if (count >= cap) return;
                if (child.IsDirectory) Walk(child);
                else count++;
            }
        }
    }

    private void DeleteNodes(IReadOnlyList<FileSystemNode> nodes, bool permanent)
    {
        if (nodes.Count == 0) return;

        long totalSize = nodes.Sum(n => n.Size);
        string what;
        if (nodes.Count == 1)
        {
            var n = nodes[0];
            what = n.IsDirectory
                ? $"a PASTA \"{n.Name}\" ({FileSystemNode.FormatSize(n.Size)}, {CountFiles(n)} arquivos)"
                : $"o arquivo \"{n.Name}\" ({FileSystemNode.FormatSize(n.Size)})";
        }
        else
        {
            what = $"{nodes.Count} itens ({FileSystemNode.FormatSize(totalSize)})";
        }

        // Permanente sempre confirma; Lixeira respeita a configuração
        if (permanent)
        {
            if (!Controls.DarkDialog.Confirm(this, "Exclusão permanente",
                    $"Excluir PERMANENTEMENTE {what}?\n\nEsta ação NÃO passa pela Lixeira e não pode ser desfeita.",
                    danger: true, confirmLabel: "Excluir permanentemente", cancelLabel: "Cancelar"))
                return;
        }
        else if (_settings.ConfirmDelete)
        {
            if (!Controls.DarkDialog.Confirm(this, "Confirmar exclusão",
                    $"Enviar {what} para a Lixeira?",
                    confirmLabel: "Enviar para a Lixeira", cancelLabel: "Cancelar"))
                return;
        }

        int okCount = 0;
        long freedBytes = 0;
        foreach (var node in nodes)
        {
            bool ok = permanent
                ? FileDeletion.Permanently(node.FullPath)
                : FileDeletion.ToRecycleBin(node.FullPath);
            if (!ok) continue;

            okCount++;
            freedBytes += node.Size;

            // Se a visão atual do mapa está dentro do nó excluído, sobe para o pai sobrevivente
            for (var v = MapPage.ViewRoot; v is not null; v = v.Parent)
                if (ReferenceEquals(v, node) && node.Parent is not null)
                {
                    MapPage.SetViewRoot(node.Parent);
                    break;
                }

            node.Parent?.Children.Remove(node);
            node.Parent?.AddSizeUpwards(-node.Size);
            node.Parent = null; // desvincula: as páginas usam isso para detectar nós excluídos
            if (_hoveredNode is not null && ReferenceEquals(_hoveredNode, node))
                _hoveredNode = null;
        }

        if (okCount == nodes.Count)
            StatusText.Text = $"{okCount} item(ns) excluído(s) — {FileSystemNode.FormatSize(freedBytes)} liberados" +
                              (permanent ? " (permanente)." : " (na Lixeira).");
        else
            StatusText.Text = $"{okCount} de {nodes.Count} excluído(s) ({FileSystemNode.FormatSize(freedBytes)} liberados). " +
                              "Alguns itens podem estar em uso ou protegidos.";

        RefreshAllPages();
    }
}
