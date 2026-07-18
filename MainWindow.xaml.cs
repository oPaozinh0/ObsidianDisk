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
    private readonly DispatcherTimer _liveRefreshTimer;
    private LiveWatcher? _watcher;
    private TrayService? _tray;
    private AppSettings _settings;

    private FileSystemNode? _scanRoot;
    private FileSystemNode? _hoveredNode;
    private CancellationTokenSource? _cts;
    private string? _lastScanPath;
    private long _scanTargetBytes;

    /// <summary>Unidades já alertadas nesta sessão (rearma quando voltam ao normal).</summary>
    private readonly HashSet<string> _diskAlerted = new(StringComparer.OrdinalIgnoreCase);

    public MainWindow()
    {
        InitializeComponent();

        _settings = AppStorage.LoadSettings();
        SettingsPage.Load(_settings);
        SettingsPage.SettingsChanged += s =>
        {
            _settings = s;
            LargeFilesPage.SetMinBytes(s.LargeFileMinBytes);
            SyncLiveWatcher();
            _tray?.SetPersistent(s.MinimizeToTray);

            // Tema/daltonismo mudam cores construídas em código (legenda, cards, mapa)
            MapPage.RebuildLegend();
            MapPage.Refresh();
            LargeFilesPage.Refresh();
            if (_scanRoot is not null) OverviewPage.UpdateFromScan(_scanRoot);
        };
        LargeFilesPage.SetMinBytes(_settings.LargeFileMinBytes);

        _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _progressTimer.Tick += (_, _) => UpdateProgressUi();

        // Debounce das atualizações do monitoramento ao vivo
        _liveRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _liveRefreshTimer.Tick += (_, _) =>
        {
            _liveRefreshTimer.Stop();
            MapPage.Refresh();
        };

        OverviewPage.ScanRequested += StartOrCancelScan;
        OverviewPage.OpenInMapRequested += node =>
        {
            MapPage.SetViewRoot(node);
            NavMap.IsChecked = true;
        };
        OverviewPage.OpenCleanupRequested += () => NavCleanup.IsChecked = true;
        OverviewPage.OpenLargeFilesRequested += () => NavLargeFiles.IsChecked = true;
        OverviewPage.OpenDuplicatesRequested += () => NavDuplicates.IsChecked = true;
        OverviewPage.OpenDiscoveriesRequested += () => NavDiscoveries.IsChecked = true;

        MapPage.HoverChanged += OnTreemapHover;
        BuildTreemapContextMenu();

        LargeFilesPage.DeleteRequested += DeleteNodes;
        DuplicatesPage.DeleteRequested += DeleteNodes;
        DiscoveriesPage.DeleteRequested += DeleteNodes;
        GoalPage.DeleteRequested += DeleteNodes;
        RulesPage.DeleteRequested += DeleteNodes;
        RulesPage.MatchesFound += (count, bytes) =>
            Notifier.Show(L.T("Rule.NotifyTitle"),
                L.F("Rule.NotifyMsg", count, FileSystemNode.FormatSize(bytes)));

        PreviewKeyDown += OnKeyDown;
        StateChanged += (_, _) => OnWindowStateChanged();

        // Bandeja do sistema + notificações nativas
        _tray = new TrayService();
        _tray.OpenRequested += RestoreFromTray;
        _tray.ExitRequested += Close;
        _tray.SetPersistent(_settings.MinimizeToTray);
        Closed += (_, _) => _tray?.Dispose();

        // "ObsidianDisk.exe <pasta>" já abre escaneando o caminho informado
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1 && Directory.Exists(args[1]))
        {
            OverviewPage.SetCustomFolder(args[1]);
            Loaded += (_, _) => StartOrCancelScan(args[1]);
        }

        Loaded += async (_, _) => await CheckForUpdatesAsync();

        // Versão na barra inferior + dicas rotativas quando a barra está ociosa
        var version = typeof(MainWindow).Assembly.GetName().Version;
        VersionText.Text = $"v{version?.ToString(3) ?? ""}";

        _tipIndex = Random.Shared.Next(TipCount);
        _tipTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _tipTimer.Tick += (_, _) =>
        {
            // só mostra piada se nenhum status "de verdade" apareceu nos últimos 20s
            if ((DateTime.Now - _lastRealStatus).TotalSeconds >= 20)
            {
                _tipIndex = (_tipIndex + 1) % TipCount;
                StatusText.Text = L.T($"Tip.{_tipIndex + 1}");
            }
        };
        _tipTimer.Start();
    }

    // ---------------- Desfazer exclusão ----------------

    /// <summary>Caminhos enviados à Lixeira nesta sessão (só eles podem voltar).</summary>
    private readonly Stack<List<string>> _undoStack = new();

    private void PushUndo(List<string> paths)
    {
        if (paths.Count == 0) return;
        _undoStack.Push(paths);
        if (UndoButton.Visibility != Visibility.Visible)
            Controls.Animate.FadeIn(UndoButton);
        UndoButton.ToolTip = L.F("Undo.Tooltip", paths.Count == 1
            ? System.IO.Path.GetFileName(paths[0])
            : L.F("Del.ManyWhat", paths.Count, ""));
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_undoStack.Count == 0) return;

        var paths = _undoStack.Pop();
        int restored = paths.Count(FileDeletion.RestoreFromRecycleBin);

        if (_undoStack.Count == 0)
            Controls.Animate.FadeOut(UndoButton);

        SetStatus(restored > 0 ? L.F("Undo.Done", restored) : L.T("Undo.Failed"));

        // A árvore em memória não tem mais os nós — só um rescan devolve os tamanhos
        if (restored > 0 && _lastScanPath is not null)
            StartOrCancelScan(_lastScanPath);
    }

    // ---------------- Barra de status ----------------

    private const int TipCount = 8;
    private DispatcherTimer? _tipTimer;
    private DateTime _lastRealStatus = DateTime.MinValue;
    private int _tipIndex;

    /// <summary>Status "de verdade" — pausa as dicas rotativas por um tempo.</summary>
    private void SetStatus(string text)
    {
        StatusText.Text = text;
        _lastRealStatus = DateTime.Now;
    }

    // ---------------- Atualizações ----------------

    private string? _updateUrl;

    private async Task CheckForUpdatesAsync()
    {
        var current = typeof(MainWindow).Assembly.GetName().Version ?? new Version(0, 0);
        var update = await UpdateChecker.CheckAsync(current);
        if (update is null) return;

        _updateUrl = update.Url;
        UpdateButton.Content = L.F("Shell.UpdateAvailable", update.Tag);
        Controls.Animate.FadeIn(UpdateButton);
    }

    private void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_updateUrl is not null)
            Process.Start(new ProcessStartInfo(_updateUrl) { UseShellExecute = true });
    }

    // ---------------- Monitoramento ao vivo ----------------

    /// <summary>Inicia/para o watcher conforme a configuração e o estado do scan.</summary>
    private void SyncLiveWatcher()
    {
        if (_settings.LiveMonitoring && _scanRoot is not null && _cts is null)
        {
            _watcher ??= CreateWatcher();
            if (!_watcher.IsRunning)
                _watcher.Start(_scanRoot);
        }
        else
        {
            _watcher?.Stop();
        }
    }

    private LiveWatcher CreateWatcher()
    {
        var watcher = new LiveWatcher(Dispatcher);
        watcher.TreeChanged += () =>
        {
            // Reagenda o refresh: várias mudanças seguidas geram um único redraw
            _liveRefreshTimer.Stop();
            _liveRefreshTimer.Start();
        };
        return watcher;
    }

    // ---------------- Janela (chrome personalizado) ----------------

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void OnWindowStateChanged()
    {
        // Minimizar esconde na bandeja quando a opção está ligada (o ícone é persistente)
        if (WindowState == WindowState.Minimized && _settings.MinimizeToTray)
        {
            Hide();
            return;
        }

        // Com WindowChrome, a janela maximizada avança sobre as bordas da tela — compensa com margem
        bool max = WindowState == WindowState.Maximized;
        RootBorder.Margin = max ? new Thickness(7) : new Thickness(0);
        // Glifos Segoe MDL2: E923 = restaurar, E922 = maximizar
        MaximizeButton.Content = max ? ((char)0xE923).ToString() : ((char)0xE922).ToString();
    }

    /// <summary>Reexibe e traz a janela de volta a partir da bandeja.</summary>
    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    // ---------------- Navegação ----------------

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (OverviewPage is null) return; // ainda inicializando

        OverviewPage.Visibility = ReferenceEquals(sender, NavOverview) ? Visibility.Visible : Visibility.Collapsed;
        MapPage.Visibility = ReferenceEquals(sender, NavMap) ? Visibility.Visible : Visibility.Collapsed;
        ComparePage.Visibility = ReferenceEquals(sender, NavCompare) ? Visibility.Visible : Visibility.Collapsed;
        LargeFilesPage.Visibility = ReferenceEquals(sender, NavLargeFiles) ? Visibility.Visible : Visibility.Collapsed;
        DuplicatesPage.Visibility = ReferenceEquals(sender, NavDuplicates) ? Visibility.Visible : Visibility.Collapsed;
        DiscoveriesPage.Visibility = ReferenceEquals(sender, NavDiscoveries) ? Visibility.Visible : Visibility.Collapsed;
        CleanupPage.Visibility = ReferenceEquals(sender, NavCleanup) ? Visibility.Visible : Visibility.Collapsed;
        GoalPage.Visibility = ReferenceEquals(sender, NavGoal) ? Visibility.Visible : Visibility.Collapsed;
        RulesPage.Visibility = ReferenceEquals(sender, NavRules) ? Visibility.Visible : Visibility.Collapsed;
        HistoryPage.Visibility = ReferenceEquals(sender, NavHistory) ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = ReferenceEquals(sender, NavSettings) ? Visibility.Visible : Visibility.Collapsed;

        // Anima a página que acabou de aparecer
        foreach (FrameworkElement page in new FrameworkElement[]
                 { OverviewPage, MapPage, ComparePage, LargeFilesPage, DuplicatesPage, DiscoveriesPage, CleanupPage, GoalPage, RulesPage, HistoryPage, SettingsPage })
            if (page.Visibility == Visibility.Visible)
                Controls.Animate.PageIn(page);

        if (ReferenceEquals(sender, NavHistory))
            HistoryPage.Reload();
        if (ReferenceEquals(sender, NavCleanup))
            CleanupPage.EnsureMeasured(); // mede os tamanhos na primeira visita
        if (ReferenceEquals(sender, NavOverview) && _scanRoot is not null)
            OverviewPage.UpdateFromScan(_scanRoot); // reflete mudanças do monitoramento ao vivo
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
            SetStatus(L.T("Scan.InvalidPath"));
            return;
        }

        _watcher?.Stop(); // pausa o monitoramento durante o rescan

        _cts = new CancellationTokenSource();
        _lastScanPath = path;
        OverviewPage.SetScanning(true);
        RescanButton.IsEnabled = false;
        Controls.Animate.FadeIn(ProgressPanel);

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
            var scannedAt = DateTime.Now;
            AppStorage.AppendHistory(new ScanRecord(scannedAt, path, root.Size, progress.FilesScanned));
            await Task.Run(() => SnapshotStore.Capture(root, path, scannedAt, progress.FilesScanned));

            if (drive is not null && drive.TotalSize > 0)
            {
                var usedPct = (int)((drive.TotalSize - drive.TotalFreeSpace) * 100 / drive.TotalSize);
                _tray?.SetTooltip(L.F("Tray.Tooltip", drive.Name, usedPct));
                MaybeAlertDiskFull(drive, path, usedPct);
            }

            RefreshAllPages();
            LastScanText.Text = L.F("Shell.LastScan", DateTime.Now.ToString("dd/MM/yyyy HH:mm"));
            SetStatus(L.F("Scan.Done", stopwatch.Elapsed.TotalSeconds.ToString("0.#"),
                progress.FilesScanned.ToString("N0"), FileSystemNode.FormatSize(root.Size)));
        }
        catch (OperationCanceledException)
        {
            SetStatus(L.T("Scan.Cancelled"));
            if (_scanRoot is not null && _scanRoot.Size > 0)
                RefreshAllPages();
        }
        catch (Exception ex)
        {
            SetStatus(L.F("Scan.Error", ex.Message));
        }
        finally
        {
            _progressTimer.Stop();
            Controls.Animate.FadeOut(ProgressPanel);
            OverviewPage.SetScanning(false);
            RescanButton.IsEnabled = _lastScanPath is not null;
            _cts.Dispose();
            _cts = null;
            SyncLiveWatcher(); // retoma o monitoramento sobre a nova árvore
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
            ProgressStats.Text = L.F("Scan.StatsPercent", p.FilesScanned.ToString("N0"), FileSystemNode.FormatSize(p.BytesScanned), percent.ToString("0.#"));
        }
        else
        {
            ProgressStats.Text = L.F("Scan.Stats", p.FilesScanned.ToString("N0"), FileSystemNode.FormatSize(p.BytesScanned));
        }

        MapPage.Refresh(); // mapa ao vivo

        // Arquivos Grandes também acompanha o scan (a cada ~1s, não a cada quadro)
        if ((DateTime.Now - _lastLargeFilesRefresh).TotalSeconds >= 1)
        {
            _lastLargeFilesRefresh = DateTime.Now;
            if (_scanRoot is not null)
                LargeFilesPage.UpdateFromScan(_scanRoot);
        }
    }

    private DateTime _lastLargeFilesRefresh = DateTime.MinValue;

    private void RefreshAllPages()
    {
        if (_scanRoot is null) return;
        OverviewPage.UpdateFromScan(_scanRoot);
        ComparePage.UpdateFromScan(_scanRoot);
        LargeFilesPage.UpdateFromScan(_scanRoot);
        DuplicatesPage.UpdateFromScan(_scanRoot);
        DiscoveriesPage.UpdateFromScan(_scanRoot);
        GoalPage.UpdateFromScan(_scanRoot);
        RulesPage.UpdateFromScan(_scanRoot);
        MapPage.Refresh();
        HistoryPage.SetScanRoot(_scanRoot);
        HistoryPage.Reload();
    }

    // ---------------- Alerta de disco cheio ----------------

    private const int DiskForecastHorizonDays = 60;

    /// <summary>
    /// Notifica quando a unidade cruza o limite de uso ou quando a projeção do histórico
    /// indica que vai encher em breve. Alerta uma vez por sessão e rearma ao normalizar.
    /// </summary>
    private void MaybeAlertDiskFull(DriveInfo drive, string path, int usedPct)
    {
        if (!_settings.DiskFullAlert) return;

        bool overThreshold = usedPct >= _settings.DiskFullThresholdPercent;

        DiskForecast? forecast = null;
        if (!overThreshold)
        {
            var history = AppStorage.LoadHistory()
                .Where(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase))
                .ToList();
            forecast = DiskForecaster.Project(history, drive.TotalFreeSpace, DateTime.Now);
        }
        bool fillingSoon = forecast is not null && forecast.DaysUntilFull < DiskForecastHorizonDays;

        if (!overThreshold && !fillingSoon)
        {
            _diskAlerted.Remove(drive.Name); // voltou ao normal: pode alertar de novo depois
            return;
        }

        if (!_diskAlerted.Add(drive.Name)) return; // já avisado nesta sessão

        if (overThreshold)
            Notifier.Show(L.T("Alert.FullTitle"),
                L.F("Alert.FullMsg", drive.Name, usedPct, FileSystemNode.FormatSize(drive.TotalFreeSpace)));
        else
            Notifier.Show(L.T("Alert.ForecastTitle"),
                L.F("Alert.ForecastMsg", drive.Name, forecast!.FullDate.ToString("Y")));
    }

    // ---------------- Hover / status ----------------

    private void OnTreemapHover(FileSystemNode? node)
    {
        _hoveredNode = node;
        var viewRoot = MapPage.ViewRoot;

        if (node is null)
        {
            if (viewRoot is not null)
                SetStatus($"{viewRoot.FullPath} — {FileSystemNode.FormatSize(viewRoot.Size)}");
            return;
        }

        double percent = viewRoot is { Size: > 0 } ? node.Size * 100.0 / viewRoot.Size : 0;
        string kind = node.IsDirectory ? L.F("Hover.Items", node.Children.Count) : L.T("Hover.File");
        SetStatus(L.F("Hover.Info", node.FullPath, FileSystemNode.FormatSize(node.Size), percent.ToString("0.#"), kind));
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
        string details = node.IsDirectory
            ? L.F("Ctx.DirDetails", FileSystemNode.FormatSize(node.Size), node.Children.Count)
            : FileSystemNode.FormatSize(node.Size);

        // Cabeçalho: deixa explícito sobre o que as ações abaixo agem
        var safety = SafetyDatabase.Lookup(node);
        if (safety is not null)
            details += $" · {SafetyDatabase.LabelOf(safety.Level)}";

        menu.Items.Add(new MenuItem
        {
            Header = $"{node.Name} — {details}",
            IsEnabled = false,
            FontWeight = FontWeights.Bold,
            // valor local vence o trigger de desabilitado — o cabeçalho fica em destaque
            Foreground = (System.Windows.Media.Brush)FindResource("Text"),
        });

        var openItem = new MenuItem { Header = L.T("Ctx.OpenExplorer") };
        openItem.Click += (_, _) =>
        {
            string args = node.IsDirectory ? $"\"{node.FullPath}\"" : $"/select,\"{node.FullPath}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
        };

        var copyItem = new MenuItem { Header = L.T("Ctx.CopyPath") };
        copyItem.Click += (_, _) => Clipboard.SetText(node.FullPath);

        var moveItem = new MenuItem { Header = L.T("Ctx.MoveTo") };
        moveItem.Click += (_, _) => MoveNode(node);

        var recycleItem = new MenuItem { Header = L.T("Ctx.DeleteRecycle") };
        recycleItem.Click += (_, _) => DeleteNodes(new[] { node }, permanent: false);

        var permanentItem = new MenuItem { Header = L.T("Ctx.DeletePermanent") };
        permanentItem.Click += (_, _) => DeleteNodes(new[] { node }, permanent: true);

        menu.Items.Add(openItem);
        menu.Items.Add(copyItem);
        menu.Items.Add(moveItem);
        menu.Items.Add(recycleItem);
        menu.Items.Add(permanentItem);
    }

    /// <summary>
    /// Realoca um arquivo/pasta para outro drive (ou pasta), liberando espaço no drive de origem.
    /// A cópia entre volumes roda em segundo plano; ao terminar, o nó sai da árvore em memória.
    /// </summary>
    private async void MoveNode(FileSystemNode node)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = L.T("Move.PickDest"),
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        string dest = dlg.SelectedPath;
        string sourceRoot = Path.GetPathRoot(node.FullPath) ?? "";
        string destRoot = Path.GetPathRoot(dest) ?? "";
        bool sameDrive = string.Equals(sourceRoot, destRoot, StringComparison.OrdinalIgnoreCase);

        string what = node.IsDirectory
            ? L.F("Del.FolderWhat", node.Name, FileSystemNode.FormatSize(node.Size), CountFiles(node))
            : L.F("Del.FileWhat", node.Name, FileSystemNode.FormatSize(node.Size));
        string note = sameDrive
            ? L.T("Move.SameDriveNote")
            : L.F("Move.FreeNote", sourceRoot, FileSystemNode.FormatSize(node.Size));

        if (!Controls.DarkDialog.Confirm(this, L.T("Move.Title"), L.F("Move.ConfirmMsg", what, dest) + note,
                confirmLabel: L.T("Move.Confirm"), cancelLabel: L.T("Del.Cancel")))
            return;

        SetStatus(L.F("Move.Working", node.Name));
        string sourcePath = node.FullPath;
        long movedSize = node.Size;
        bool ok = await Task.Run(() => FileDeletion.Move(sourcePath, dest));

        if (!ok)
        {
            SetStatus(L.T("Move.Failed"));
            return;
        }

        // O item deixou o local escaneado: reflete na árvore como numa exclusão
        for (var v = MapPage.ViewRoot; v is not null; v = v.Parent)
            if (ReferenceEquals(v, node) && node.Parent is not null)
            {
                MapPage.SetViewRoot(node.Parent);
                break;
            }
        node.Parent?.Children.Remove(node);
        node.Parent?.AddSizeUpwards(-node.Size);
        node.Parent = null;
        if (_hoveredNode is not null && ReferenceEquals(_hoveredNode, node))
            _hoveredNode = null;

        SetStatus(L.F("Move.Done", node.Name, FileSystemNode.FormatSize(movedSize), dest));
        RefreshAllPages();
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
                ? L.F("Del.FolderWhat", n.Name, FileSystemNode.FormatSize(n.Size), CountFiles(n))
                : L.F("Del.FileWhat", n.Name, FileSystemNode.FormatSize(n.Size));
        }
        else
        {
            what = L.F("Del.ManyWhat", nodes.Count, FileSystemNode.FormatSize(totalSize));
        }

        // Se algum alvo é conhecido e arriscado, o diálogo diz por quê
        string safetyNote = "";
        var risky = nodes.Select(SafetyDatabase.Lookup)
                         .Where(s => s is { Level: SafetyLevel.Never or SafetyLevel.Caution })
                         .OrderByDescending(s => s!.Level)
                         .FirstOrDefault();
        if (risky is not null)
            safetyNote = L.F("Del.SafetyWarning", SafetyDatabase.LabelOf(risky.Level),
                risky.Description + (risky.Advice is null ? "" : " " + risky.Advice));

        // Permanente sempre confirma; Lixeira respeita a configuração
        if (permanent)
        {
            if (!Controls.DarkDialog.Confirm(this, L.T("Del.PermTitle"), L.F("Del.PermMsg", what) + safetyNote,
                    danger: true, confirmLabel: L.T("Del.PermConfirm"), cancelLabel: L.T("Del.Cancel")))
                return;
        }
        else if (_settings.ConfirmDelete || risky is not null)
        {
            if (!Controls.DarkDialog.Confirm(this, L.T("Del.RecycleTitle"), L.F("Del.RecycleMsg", what) + safetyNote,
                    confirmLabel: L.T("Del.RecycleConfirm"), cancelLabel: L.T("Del.Cancel")))
                return;
        }

        int okCount = 0;
        long freedBytes = 0;
        var undoPaths = new List<string>();
        foreach (var node in nodes)
        {
            bool ok = permanent
                ? FileDeletion.Permanently(node.FullPath)
                : FileDeletion.ToRecycleBin(node.FullPath);
            if (!ok) continue;

            okCount++;
            freedBytes += node.Size;
            if (!permanent) undoPaths.Add(node.FullPath);

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
            SetStatus(L.F("Del.DoneOk", okCount, FileSystemNode.FormatSize(freedBytes),
                permanent ? L.T("Del.SuffixPermanent") : L.T("Del.SuffixRecycle")));
        else
            SetStatus(L.F("Del.DonePartial", okCount, nodes.Count, FileSystemNode.FormatSize(freedBytes)));

        PushUndo(undoPaths); // exclusão permanente não entra: não há como voltar
        RefreshAllPages();
    }
}
