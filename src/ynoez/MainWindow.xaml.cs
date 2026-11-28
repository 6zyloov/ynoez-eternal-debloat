using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Interop;
using Microsoft.Win32;
using ynoez.Services;

namespace ynoez;

public partial class MainWindow : Window
{
    private static readonly Brush PageBrush = BrushFrom("#000000");
    private static readonly Brush CardBrush = BrushFrom("#080808");
    private static readonly Brush AccentBrush = BrushFrom("#FFFFFF");
    private static readonly Brush PrimaryBrush = BrushFrom("#FFFFFF");
    private static readonly Brush SecondaryBrush = BrushFrom("#D2D2D2");
    private static readonly Brush MutedBrush = BrushFrom("#858585");
    private static readonly Brush BorderBrushSoft = BrushFrom("#242424");

    private HardwareSnapshot _hardware = new();
    private string _currentPage = "Home";
    private bool _isBusy;
    private CancellationTokenSource? _networkMonitorCts;
    private CancellationTokenSource? _networkDiagnosticCts;
    private CancellationTokenSource? _appxInventoryCts;
    private CancellationTokenSource? _systemAppxInventoryCts;
    private CancellationTokenSource? _programInventoryCts;
    private CancellationTokenSource? _autoProfileCts;
    private CancellationTokenSource? _metricsCts;
    private CancellationTokenSource? _benchmarkCts;
    private CancellationTokenSource? _integrityAuditCts;
    private CancellationTokenSource? _windowsRepairCts;
    private CancellationTokenSource? _memoryInventoryCts;
    private CancellationTokenSource? _motherboardDeviceInventoryCts;
    private CancellationTokenSource? _serviceInventoryCts;
    private CancellationTokenSource? _scheduledTaskInventoryCts;
    private bool _allowClose;
    private bool _sessionEnding;
    private bool _trayIconVisible;
    private HwndSource? _windowSource;
    private bool _trayHookAttached;

    private const uint TrayNimAdd = 0x00000000;
    private const uint TrayNimDelete = 0x00000002;
    private const uint TrayNifMessage = 0x00000001;
    private const uint TrayNifIcon = 0x00000002;
    private const uint TrayNifTip = 0x00000004;
    private const int TrayCallbackMessage = 0x8000 + 41;
    private static readonly int TaskbarCreatedMessage = unchecked((int)RegisterWindowMessage("TaskbarCreated"));
    private const int WmLeftButtonDoubleClick = 0x0203;
    private const int WmRightButtonUp = 0x0205;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public IntPtr WindowHandle;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string ToolTip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid ItemGuid;
        public IntPtr BalloonIcon;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", EntryPoint = "LoadIconW", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadSystemIcon(IntPtr instance, IntPtr iconName);

    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    private sealed record ToolboxEntry(string Name, string Description, string[] Keywords, string Target, string Kind = "Configuração", string? ServiceName = null);

    private static readonly ToolboxEntry[] ToolboxCatalog =
    {
        new("Windows Update", "Abrir a página oficial de atualizações do Windows. O ynoez não bloqueia o Windows Update.", new[] { "update", "atualização", "windows update", "wuauserv" }, "ms-settings:windowsupdate"),
        new("Windows Search", "Abrir configurações de pesquisa e preservar a pesquisa do menu Iniciar.", new[] { "search", "pesquisa", "wsearch", "indexação", "indexacao" }, "ms-settings:search"),
        new("Microsoft Store", "Abrir a Microsoft Store e manter os serviços de instalação/licença preservados.", new[] { "store", "loja", "clipsvc", "installservice" }, "ms-windows-store:"),
        new("Impressoras", "Abrir impressoras e scanners. O serviço Spooler não é desativado.", new[] { "printer", "print", "impressão", "impressora", "spooler" }, "ms-settings:printers"),
        new("Área de transferência", "Abrir as configurações da Área de Transferência do Windows.", new[] { "clipboard", "área de transferência", "copiar", "colar" }, "ms-settings:clipboard"),
        new("Windows Security", "Abrir a Segurança do Windows. Defender e proteções não são desligados.", new[] { "defender", "security", "segurança", "antivirus", "antivírus", "smartscreen" }, "windowsdefender:"),
        new("Energia e suspensão", "Abrir as configurações oficiais de energia e suspensão.", new[] { "power", "energia", "suspensão", "suspensao", "plano de energia" }, "ms-settings:powersleep"),
        new("Modo de Jogo", "Abrir o Modo de Jogo do Windows sem remover recursos de captura.", new[] { "game mode", "modo de jogo", "gaming", "jogo" }, "ms-settings:gaming-gamemode"),
        new("Bluetooth", "Abrir configurações Bluetooth. Drivers e serviços não são removidos.", new[] { "bluetooth", "bt" }, "ms-settings:bluetooth"),
        new("Rede e Internet", "Abrir o status da conexão, Wi-Fi e Ethernet.", new[] { "network", "rede", "internet", "ethernet", "wifi", "wi-fi", "lan" }, "ms-settings:network-status"),
        new("Serviço Windows Update", "Estado do serviço Windows Update. O ynoez não desativa o serviço.", new[] { "wuauserv", "serviço update", "windows update service" }, "services.msc", "SERVIÇO", "wuauserv"),
        new("NVIDIA Display Container", "Estado do componente de contêiner do driver NVIDIA. Não é desligado para preservar painel e recursos de overlay.", new[] { "nvidia container", "nvdisplay", "alt z", "instant replay" }, "services.msc", "SERVIÇO", "NVDisplay.ContainerLocalSystem"),
        new("Pesquisa do Windows", "Estado do serviço Windows Search. A pesquisa do menu Iniciar é preservada.", new[] { "wsearch", "serviço de pesquisa", "indexador" }, "services.msc", "SERVIÇO", "WSearch"),
        new("Spooler de Impressão", "Estado do serviço de impressão. Não é removido nem desativado pelo ynoez.", new[] { "spooler", "impressão", "impressora" }, "services.msc", "SERVIÇO", "Spooler"),
        new("BITS", "Estado do Serviço de Transferência Inteligente em Segundo Plano, usado por recursos de atualização.", new[] { "bits", "transferência em segundo plano", "delivery" }, "services.msc", "SERVIÇO", "BITS"),
        new("Microsoft Store — Licenciamento", "Estado do serviço de licenciamento da Store. A aplicação mantém este recurso protegido.", new[] { "clipsvc", "licenciamento store", "license manager" }, "services.msc", "SERVIÇO", "ClipSVC"),
        new("Aplicativos instalados", "Abrir a lista oficial de aplicativos instalados.", new[] { "apps", "aplicativos", "programas", "desinstalar" }, "ms-settings:appsfeatures"),
        new("Calculadora", "Abrir a Calculadora do Windows. O aplicativo é preservado pelo perfil de debloat.", new[] { "calculator", "calculadora", "calc.exe" }, "calc.exe", "Aplicativo do Windows"),
        new("Bloco de Notas", "Abrir o Bloco de Notas. O aplicativo é preservado pelo perfil de debloat.", new[] { "notepad", "bloco de notas", "notepad.exe" }, "notepad.exe", "Aplicativo do Windows"),
        new("Xbox Game Bar e Capturas", "Abrir as configurações oficiais de Game Bar e capturas. Overlays e integração de jogos não são removidos.", new[] { "xbox", "game bar", "captura", "overlay", "clip" }, "ms-settings:gaming-gamebar"),
        new("Gerenciador de Dispositivos", "Ver drivers e hardware instalados.", new[] { "device manager", "gerenciador de dispositivos", "drivers", "driver", "hardware" }, "devmgmt.msc", "Ferramenta do Windows"),
        new("Serviços do Windows", "Consultar serviços do Windows. Alterações de inicialização permanecem protegidas.", new[] { "services", "serviços", "services.msc", "wuauserv", "spooler", "sysmain" }, "services.msc", "Ferramenta do Windows"),
        new("Gerenciador de Tarefas", "Ver processos, inicialização e consumo de recursos.", new[] { "task manager", "gerenciador de tarefas", "processos", "startup", "inicialização" }, "taskmgr.exe", "Ferramenta do Windows"),
        new("Painel de Controle NVIDIA", "Abrir o painel NVIDIA se estiver instalado pelo driver oficial.", new[] { "nvidia", "gpu", "placa de vídeo", "placa de video", "nvcplui" }, "nvcplui.exe", "Aplicativo instalado")
    };

    public MainWindow()
    {
        InitializeComponent();
        SetNavigationState("Home");
        Application.Current.SessionEnding += Application_SessionEnding;
        Closed += (_, _) =>
        {
            Application.Current.SessionEnding -= Application_SessionEnding;
            RemoveTrayIcon();
            if (_trayHookAttached) _windowSource?.RemoveHook(TrayWindowProc);
            _trayHookAttached = false;
            _appxInventoryCts?.Cancel();
            _systemAppxInventoryCts?.Cancel();
            _programInventoryCts?.Cancel();
            _networkDiagnosticCts?.Cancel();
            _networkMonitorCts?.Cancel();
            _autoProfileCts?.Cancel();
            _metricsCts?.Cancel();
            _benchmarkCts?.Cancel();
            _integrityAuditCts?.Cancel();
            _windowsRepairCts?.Cancel();
            _memoryInventoryCts?.Cancel();
            _motherboardDeviceInventoryCts?.Cancel();
            _serviceInventoryCts?.Cancel();
            _scheduledTaskInventoryCts?.Cancel();
        };
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _windowSource = HwndSource.FromHwnd(handle);
        if (_windowSource is not null)
        {
            _windowSource.AddHook(TrayWindowProc);
            _trayHookAttached = true;
        }
        await RefreshHardwareAsync();
        ShowPage("Home");
        if (ProcessPriorityProfileService.LoadProfile().Enabled)
            StartProcessFocusMonitor();
    }

    private async Task RefreshHardwareAsync()
    {
        if (_isBusy) return;
        _isBusy = true;
        try
        {
            _hardware = await HardwareService.GetSnapshotAsync();
            AppLog.Write("Inventário de hardware atualizado.");
            if (_currentPage == "Home" || _currentPage == "Motherboard") ShowPage(_currentPage);
        }
        catch (Exception ex)
        {
            AppLog.Write("Falha ao atualizar hardware: " + ex.Message);
            MessageBox.Show("Não foi possível atualizar todas as informações do hardware.\n\n" + ex.Message, "ynoez", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { _isBusy = false; }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshHardwareAsync();
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Content is null) return;
        ShowPage(button.Content.ToString() ?? "Home");
    }

    private void ShowPage(string page)
    {
        _currentPage = page;
        PageTitle.Text = page;
        SetNavigationState(page);
        PageScroll.ScrollToTop();
        PageHost.Content = page switch
        {
            "Home" => BuildHome(),
            "Monitor" => BuildMonitor(),
            "Foco de Jogo" => BuildProcessFocusPage(),
            "Backup" => BuildBackup(),
            "Tweaks" => BuildTweaks(),
            "Network" => BuildNetwork(),
            "Debloat" => BuildDebloat(),
            "Motherboard" => BuildMotherboard(),
            "Toolbox" => BuildToolbox(),
            "Restore" => BuildRestore(),
            "Logs" => BuildLogs(),
            _ => BuildHome()
        };
    }

    private void SetNavigationState(string page)
    {
        var map = new Dictionary<string, Button>
        {
            ["Home"] = NavHome, ["Monitor"] = NavMonitor, ["Foco de Jogo"] = NavProcessFocus, ["Backup"] = NavBackup, ["Tweaks"] = NavTweaks,
            ["Network"] = NavNetwork, ["Debloat"] = NavDebloat, ["Motherboard"] = NavBoard,
            ["Toolbox"] = NavToolbox, ["Restore"] = NavRestore, ["Logs"] = NavLogs
        };
        foreach (var item in map)
        {
            item.Value.Background = item.Key == page ? BrushFrom("#181818") : PageBrush;
            item.Value.Foreground = item.Key == page ? AccentBrush : PrimaryBrush;
        }
    }

    private UIElement BuildHome()
    {
        var root = PageContainer();
        root.Children.Add(PageIntro("Visão geral", "Seu hardware e os atalhos que realmente importam."));

        var grid = new UniformGrid { Columns = 2, Rows = 2, Margin = new Thickness(0, 3, 0, 16) };
        grid.Children.Add(InfoTile("PROCESSADOR", _hardware.Cpu, "CPU"));
        grid.Children.Add(InfoTile("PLACA DE VÍDEO", _hardware.Gpu, "GPU"));
        grid.Children.Add(InfoTile("MEMÓRIA RAM", _hardware.Memory, "RAM"));
        grid.Children.Add(InfoTile("WINDOWS", _hardware.OperatingSystem + " • Build " + _hardware.WindowsBuild, "OS"));
        root.Children.Add(grid);

        var actionsCard = MakeCard();
        var actionsPanel = new StackPanel { Margin = new Thickness(20, 17, 20, 17) };
        actionsPanel.Children.Add(Heading("Ações rápidas", 15));
        var actions = new UniformGrid { Columns = 3, Margin = new Thickness(0, 8, 0, 0) };
        var optimize = SmallAction("Otimizar para jogos", () => ShowPage("Tweaks"));
        var debloat = SmallAction("Limpar / Debloat", () => ShowPage("Debloat"));
        var monitor = SmallAction("Monitorar desempenho", () => ShowPage("Monitor"));
        optimize.Margin = new Thickness(0, 0, 8, 0);
        debloat.Margin = new Thickness(0, 0, 8, 0);
        actions.Children.Add(optimize);
        actions.Children.Add(debloat);
        actions.Children.Add(monitor);
        actionsPanel.Children.Add(actions);
        actionsCard.Child = actionsPanel;
        root.Children.Add(actionsCard);

        var detailCard = MakeCard();
        var detailPanel = new StackPanel { Margin = new Thickness(20, 15, 20, 15) };
        detailPanel.Children.Add(Heading("Sistema", 13));
        detailPanel.Children.Add(KeyValue("Placa-mãe", _hardware.BoardProduct));
        detailPanel.Children.Add(KeyValue("Rede", _hardware.NetworkSummary));
        var detailNote = Body("As alterações são locais e registradas. Perfis e limpeza podem ser restaurados quando houver backup.");
        detailNote.Margin = new Thickness(0, 9, 0, 0);
        detailPanel.Children.Add(detailNote);
        detailCard.Child = detailPanel;
        root.Children.Add(detailCard);
        return root;
    }

    private UIElement BuildMonitor()
    {
        var root = PageContainer();
        root.Children.Add(PageIntro("Monitor", "Acompanhe CPU, memória e GPU em tempo real. Sensores indisponíveis são identificados sem instalar drivers extras."));
        var card = MakeCard();
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(Heading("Telemetria do sistema", 15));
        panel.Children.Add(Body("CPU e memória usam APIs nativas do Windows. GPU usa NVIDIA SMI quando disponível ou contadores de mecanismo 3D do Windows. Temperatura, memória dedicada e potência dependem do suporte do driver NVIDIA."));
        var cpu = MetricLine("CPU", "Aguardando leitura…");
        var memory = MetricLine("RAM", "Aguardando leitura…");
        var gpu = MetricLine("GPU 3D", "Aguardando leitura…");
        var temperature = MetricLine("Temperatura GPU", "Indisponível até a primeira leitura");
        var gpuMemory = MetricLine("Memória GPU", "Indisponível até a primeira leitura");
        var gpuPower = MetricLine("Potência GPU", "Indisponível até a primeira leitura");
        var source = Body("Fonte GPU: —");
        var status = Body("Monitor parado.");
        var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var start = new Button { Content = "Iniciar monitor", Style = (Style)FindResource("AccentButton"), MinWidth = 125, Margin = new Thickness(0, 0, 8, 0) };
        var stop = new Button { Content = "Parar", Style = (Style)FindResource("QuietButton"), MinWidth = 85, IsEnabled = false };
        start.Click += async (_, _) =>
        {
            if (_metricsCts is not null) return;
            _metricsCts = new CancellationTokenSource();
            var token = _metricsCts.Token;
            var service = new PerformanceMonitorService();
            start.IsEnabled = false;
            stop.IsEnabled = true;
            status.Text = "Coletando métricas…";
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var sample = await service.SampleAsync();
                    cpu.Value.Text = sample.CpuPercent.HasValue ? sample.CpuPercent.Value.ToString("0.0") + "%" : "Aquecendo amostra…";
                    memory.Value.Text = sample.MemoryUsedGb.ToString("0.0") + " / " + sample.MemoryTotalGb.ToString("0.0") + " GB (" + sample.MemoryPercent.ToString("0") + "%)";
                    gpu.Value.Text = sample.Gpu3DPercent.HasValue ? sample.Gpu3DPercent.Value.ToString("0.0") + "%" : "Não disponível";
                    temperature.Value.Text = sample.GpuTemperatureC.HasValue ? sample.GpuTemperatureC.Value.ToString("0") + " °C" : "Não fornecida pelo driver";
                    gpuMemory.Value.Text = sample.GpuMemoryUsedMb.HasValue && sample.GpuMemoryTotalMb.HasValue
                        ? sample.GpuMemoryUsedMb.Value.ToString("0") + " / " + sample.GpuMemoryTotalMb.Value.ToString("0") + " MB"
                        : "Não disponível";
                    gpuPower.Value.Text = sample.GpuPowerWatts.HasValue ? sample.GpuPowerWatts.Value.ToString("0.0") + " W" : "Não disponível";
                    source.Text = "Fonte GPU: " + sample.GpuTelemetrySource + " • " + sample.Timestamp.ToString("HH:mm:ss");
                    status.Text = "Monitoramento ativo • atualização aproximada de 2 segundos";
                    await Task.Delay(2000, token);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                status.Text = "Monitor parado.";
            }
            catch (Exception ex)
            {
                AppLog.Write("Monitor de hardware falhou: " + ex);
                status.Text = "Falha: " + ex.Message;
            }
            finally
            {
                _metricsCts?.Dispose();
                _metricsCts = null;
                start.IsEnabled = true;
                stop.IsEnabled = false;
            }
        };
        stop.Click += (_, _) => _metricsCts?.Cancel();
        buttons.Children.Add(start);
        buttons.Children.Add(stop);
        foreach (var line in new[] { cpu, memory, gpu, temperature, gpuMemory, gpuPower }) panel.Children.Add(line.Container);
        panel.Children.Add(source);
        panel.Children.Add(status);
        panel.Children.Add(buttons);
        card.Child = panel;
        root.Children.Add(card);

        var benchmarkCard = MakeCard();
        var benchmarkPanel = new StackPanel { Margin = new Thickness(20) };
        benchmarkPanel.Children.Add(Heading("Benchmark de FPS e frametime", 15));
        benchmarkPanel.Children.Add(Body("Captura frames reais do jogo com PresentMon oficial. Na primeira captura, o executável será baixado do GitHub e validado por SHA-256. A captura pode solicitar UAC para obter permissão ETW e não instala driver de kernel. Compatibilidade com Vanguard/FACEIT ainda precisa de teste no PC; se o anti-cheat bloquear a captura, não force."));
        var targetRow = new WrapPanel { Margin = new Thickness(0, 10, 0, 8) };
        var targetBox = new ComboBox
        {
            MinWidth = 280, Height = 34, Background = CardBrush, Foreground = PrimaryBrush,
            BorderBrush = BorderBrushSoft, DisplayMemberPath = nameof(CaptureTarget.DisplayName),
            ItemsSource = PresentMonService.GetRunningTargets()
        };
        if (targetBox.Items.Count > 0) targetBox.SelectedIndex = 0;
        var refreshTargets = new Button { Content = "Atualizar jogos", Style = (Style)FindResource("QuietButton"), Margin = new Thickness(8, 0, 0, 0) };
        refreshTargets.Click += (_, _) =>
        {
            targetBox.ItemsSource = PresentMonService.GetRunningTargets();
            if (targetBox.Items.Count > 0) targetBox.SelectedIndex = 0;
        };
        targetRow.Children.Add(targetBox);
        targetRow.Children.Add(refreshTargets);
        var durationBox = new ComboBox
        {
            MinWidth = 140, Height = 32, Background = CardBrush, Foreground = PrimaryBrush,
            BorderBrush = BorderBrushSoft, Margin = new Thickness(0, 0, 0, 8)
        };
        foreach (var seconds in new[] { 30, 60, 120, 180 })
            durationBox.Items.Add(new ComboBoxItem { Content = seconds + " segundos", Tag = seconds });
        durationBox.SelectedIndex = 1;
        var benchmarkStatus = Body("Nenhuma captura executada.");
        var benchmarkButtons = new WrapPanel();
        var startBenchmark = new Button { Content = "Iniciar benchmark", Style = (Style)FindResource("AccentButton"), MinWidth = 145, Margin = new Thickness(0, 0, 8, 0) };
        var stopBenchmark = new Button { Content = "Parar captura", Style = (Style)FindResource("QuietButton"), MinWidth = 115, IsEnabled = false };
        startBenchmark.Click += async (_, _) =>
        {
            if (_benchmarkCts is not null) return;
            if (targetBox.SelectedItem is not CaptureTarget target)
            {
                MessageBox.Show("Abra um jogo compatível e clique em Atualizar jogos.", "ynoez", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var seconds = durationBox.SelectedItem is ComboBoxItem item && item.Tag is int value ? value : 60;
            _benchmarkCts = new CancellationTokenSource();
            var token = _benchmarkCts.Token;
            startBenchmark.IsEnabled = false;
            stopBenchmark.IsEnabled = true;
            try
            {
                var result = await PresentMonService.CaptureAsync(target, seconds,
                    message => benchmarkStatus.Text = message, token);
                benchmarkStatus.Text =
                    "Jogo: " + result.ProcessName + " | Frames: " + result.FrameCount +
                    "\nFPS médio: " + result.AverageFps.ToString("0.0") +
                    " | 1% low: " + result.OnePercentLowFps.ToString("0.0") +
                    " | 0,1% low: " + result.OneTenthPercentLowFps.ToString("0.0") +
                    "\nFrametime médio: " + result.AverageFrameTimeMs.ToString("0.00") + " ms" +
                    " | P99: " + result.P99FrameTimeMs.ToString("0.00") + " ms" +
                    " | P99,9: " + result.P99_9FrameTimeMs.ToString("0.00") + " ms" +
                    "\nFrames acima de 33,3 ms: " + result.FramesOver33Ms +
                    " | acima de 50 ms: " + result.FramesOver50Ms;
                var previousBenchmark = PresentMonService.GetPreviousBenchmark(result.ProcessName, result.CsvPath);
                if (previousBenchmark is not null)
                {
                    var comparison = PresentMonService.Compare(result, previousBenchmark);
                    benchmarkStatus.Text +=
                        "\nComparação com a captura anterior (" + previousBenchmark.CapturedAt.ToLocalTime().ToString("dd/MM HH:mm") + "):" +
                        " FPS médio " + comparison.AverageFpsChangePercent.ToString("+0.0;-0.0;0.0") + "%" +
                        " | 1% low " + comparison.OnePercentLowChangePercent.ToString("+0.0;-0.0;0.0") + "%" +
                        " | 0,1% low " + comparison.OneTenthPercentLowChangePercent.ToString("+0.0;-0.0;0.0") + "%" +
                        " | frametime " + comparison.AverageFrameTimeChangePercent.ToString("+0.0;-0.0;0.0") + "%";
                }
                benchmarkStatus.Text += "\nCSV: " + result.CsvPath +
                    "\nCompare a mesma cena, duração e configurações. Variações também refletem mapa, jogadores, temperatura e processos em segundo plano.";
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                benchmarkStatus.Text = "Captura interrompida.";
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                benchmarkStatus.Text = "A permissão ETW/UAC foi cancelada. Nenhuma captura foi executada.";
            }
            catch (Exception ex)
            {
                AppLog.Write("Benchmark PresentMon falhou: " + ex);
                benchmarkStatus.Text = "Falha no benchmark: " + ex.Message;
            }
            finally
            {
                _benchmarkCts?.Dispose();
                _benchmarkCts = null;
                startBenchmark.IsEnabled = true;
                stopBenchmark.IsEnabled = false;
            }
        };
        stopBenchmark.Click += (_, _) => _benchmarkCts?.Cancel();
        benchmarkButtons.Children.Add(startBenchmark);
        benchmarkButtons.Children.Add(stopBenchmark);
        benchmarkPanel.Children.Add(targetRow);
        benchmarkPanel.Children.Add(durationBox);
        benchmarkPanel.Children.Add(benchmarkStatus);
        benchmarkPanel.Children.Add(benchmarkButtons);
        benchmarkCard.Child = benchmarkPanel;
        root.Children.Add(benchmarkCard);

        var sessionCard = MakeCard();
        var sessionPanel = new StackPanel { Margin = new Thickness(20) };
        sessionPanel.Children.Add(Heading("Comparação A/B com três rodadas", 15));
        sessionPanel.Children.Add(Body("Cria uma referência com 3 capturas e depois compara com outras 3 capturas do mesmo jogo e duração. Usa a mediana das rodadas para reduzir ruído. Mantenha o mesmo mapa/cena, resolução, limite de FPS e condições térmicas. O ynoez registra as notas, mas não controla a cena nem garante condições idênticas."));
        var sceneNotes = new TextBox
        {
            Text = "",
            MinWidth = 300,
            Height = 34,
            FontSize = 11,
            Background = BrushFrom("#050505"),
            Foreground = PrimaryBrush,
            BorderBrush = BorderBrushSoft,
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 8, 0, 8),
            ToolTip = "Anote mapa, modo, local/cena, resolução, limite de FPS e demais condições mantidas."
        };
        var sceneLabel = Body("Notas da cena/configuração (use a mesma descrição nas duas fases):");
        var sessionStatus = Body("Nenhuma referência A/B salva para este jogo.");
        var sessionButtons = new WrapPanel();
        var createBaseline = new Button { Content = "Criar referência (3x)", Style = (Style)FindResource("AccentButton"), Margin = new Thickness(0, 0, 8, 8) };
        var recordAfter = new Button { Content = "Comparar após ajustes (3x)", Style = (Style)FindResource("QuietButton"), Margin = new Thickness(0, 0, 8, 8) };
        var cancelSeries = new Button { Content = "Cancelar série", Style = (Style)FindResource("QuietButton"), IsEnabled = false, Margin = new Thickness(0, 0, 8, 8) };
        async Task RunBenchmarkSeriesAsync(bool baselinePhase)
        {
            if (_benchmarkCts is not null) return;
            if (targetBox.SelectedItem is not CaptureTarget selectedTarget)
            {
                MessageBox.Show("Abra um jogo compatível e clique em Atualizar jogos.", "ynoez", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var selectedDuration = durationBox.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag is int selectedSeconds
                ? selectedSeconds : 60;
            var sessionService = new BenchmarkSessionService();
            BenchmarkSessionDocument? reference = null;
            if (!baselinePhase)
            {
                reference = sessionService.FindLatestReference(selectedTarget.ProcessName, selectedDuration);
                if (reference is null)
                {
                    MessageBox.Show("Não há referência pendente de comparação para este jogo e duração. Crie primeiro uma referência com 3 rodadas.", "ynoez", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }

            var message = baselinePhase
                ? "O ynoez fará 3 capturas consecutivas de " + selectedDuration + " segundos. Mantenha o jogo na mesma cena e não altere configurações durante as rodadas. A permissão ETW/UAC pode aparecer em cada captura. Criar a referência?"
                : "O ynoez fará 3 capturas consecutivas de " + selectedDuration + " segundos para comparar com a referência salva. Reaplique apenas os ajustes que deseja avaliar e mantenha a mesma cena/configuração. A permissão ETW/UAC pode aparecer em cada captura. Continuar?";
            if (MessageBox.Show(message, "Benchmark A/B", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            var notes = sceneNotes.Text.Trim();
            _benchmarkCts = new CancellationTokenSource();
            var token = _benchmarkCts.Token;
            createBaseline.IsEnabled = false;
            recordAfter.IsEnabled = false;
            startBenchmark.IsEnabled = false;
            stopBenchmark.IsEnabled = false;
            cancelSeries.IsEnabled = true;
            var runs = new List<FrameBenchmarkSummary>(3);
            try
            {
                for (var index = 0; index < 3; index++)
                {
                    var currentRun = index + 1;
                    sessionStatus.Text = (baselinePhase ? "Referência" : "Pós-ajustes") +
                        " • rodada " + currentRun + "/3: preparando captura de " + selectedTarget.ProcessName + "…";
                    var result = await PresentMonService.CaptureAsync(selectedTarget, selectedDuration,
                        messageText => sessionStatus.Text = (baselinePhase ? "Referência" : "Pós-ajustes") +
                            " • rodada " + currentRun + "/3 • " + messageText, token);
                    runs.Add(result);
                    if (index < 2) await Task.Delay(2000, token);
                }

                if (baselinePhase)
                {
                    var saved = sessionService.CreateBaseline(selectedTarget.ProcessName, selectedDuration, notes, runs);
                    var median = BenchmarkSessionService.Aggregate(runs);
                    sessionStatus.Text = "Referência salva • 3 rodadas • mediana " +
                        median.MedianAverageFps.ToString("0.0") + " FPS • 1% low " +
                        median.MedianOnePercentLowFps.ToString("0.0") + " FPS • variação entre rodadas " +
                        median.AverageFpsSpreadPercent.ToString("0.0") + "% • ID " + saved.Id;
                }
                else
                {
                    var comparison = sessionService.RecordAfterRuns(reference!.Id, notes, runs);
                    sessionStatus.Text =
                        "RESULTADO A/B — " + comparison.ProcessName + " • " + comparison.DurationSeconds + " s por rodada • 3 + 3 rodadas" +
                        "\nReferência: " + comparison.Baseline.MedianAverageFps.ToString("0.0") + " FPS médios (mediana), 1% low " +
                            comparison.Baseline.MedianOnePercentLowFps.ToString("0.0") + ", 0,1% low " + comparison.Baseline.MedianOneTenthPercentLowFps.ToString("0.0") +
                        "\nPós-ajustes: " + comparison.After.MedianAverageFps.ToString("0.0") + " FPS médios (mediana), 1% low " +
                            comparison.After.MedianOnePercentLowFps.ToString("0.0") + ", 0,1% low " + comparison.After.MedianOneTenthPercentLowFps.ToString("0.0") +
                        "\nVariação: FPS médio " + comparison.Changes.AverageFpsChangePercent.ToString("+0.0;-0.0;0.0") +
                            "% • 1% low " + comparison.Changes.OnePercentLowChangePercent.ToString("+0.0;-0.0;0.0") +
                            "% • 0,1% low " + comparison.Changes.OneTenthPercentLowChangePercent.ToString("+0.0;-0.0;0.0") +
                            "% • frametime " + comparison.Changes.AverageFrameTimeChangePercent.ToString("+0.0;-0.0;0.0") + "%" +
                        "\nDispersão do FPS entre rodadas: base " + comparison.Baseline.AverageFpsSpreadPercent.ToString("0.0") +
                            "% • pós " + comparison.After.AverageFpsSpreadPercent.ToString("0.0") + "%" +
                        "\nCena base: " + (string.IsNullOrWhiteSpace(comparison.SceneNotes) ? "sem notas" : comparison.SceneNotes) +
                        "\nCena pós: " + (string.IsNullOrWhiteSpace(comparison.AfterSceneNotes) ? "sem notas" : comparison.AfterSceneNotes) +
                        "\nOs números são medianas das capturas, não uma prova isolada de causalidade. Compare se mapa, resolução, temperatura, jogadores e processos estavam equivalentes.";
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                sessionStatus.Text = "Série de benchmark cancelada. Capturas já concluídas foram preservadas como CSVs individuais; a série incompleta não foi salva como referência.";
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                sessionStatus.Text = "A permissão ETW/UAC foi cancelada. A série não foi salva.";
            }
            catch (Exception ex)
            {
                AppLog.Write("Benchmark A/B falhou: " + ex);
                sessionStatus.Text = "Falha na série de benchmark: " + ex.Message;
            }
            finally
            {
                _benchmarkCts?.Dispose();
                _benchmarkCts = null;
                createBaseline.IsEnabled = true;
                recordAfter.IsEnabled = true;
                startBenchmark.IsEnabled = true;
                stopBenchmark.IsEnabled = false;
                cancelSeries.IsEnabled = false;
            }
        }
        createBaseline.Click += async (_, _) => await RunBenchmarkSeriesAsync(true);
        recordAfter.Click += async (_, _) => await RunBenchmarkSeriesAsync(false);
        cancelSeries.Click += (_, _) => _benchmarkCts?.Cancel();
        sessionButtons.Children.Add(createBaseline);
        sessionButtons.Children.Add(recordAfter);
        sessionButtons.Children.Add(cancelSeries);
        sessionPanel.Children.Add(sceneLabel);
        sessionPanel.Children.Add(sceneNotes);
        sessionPanel.Children.Add(sessionStatus);
        sessionPanel.Children.Add(sessionButtons);
        sessionPanel.Children.Add(NoteCard("Como interpretar a comparação", "Uma diferença pequena pode ser apenas variação normal. A dispersão das três rodadas ajuda a identificar medições instáveis; não transforme uma diferença de poucos pontos percentuais em promessa de FPS. O perfil/driver, mapa, limite de FPS e temperatura precisam ser comparáveis."));
        sessionCard.Child = sessionPanel;
        root.Children.Add(sessionCard);

        root.Children.Add(NoteCard("Precisão e compatibilidade", "O ynoez não instala driver de sensor nem altera tensão, clock, curva de ventoinha ou BIOS. Alguns sensores exigem permissões ou suporte específico do fabricante e podem aparecer como indisponíveis."));
        return root;
    }

    private static (Border Container, TextBlock Value) MetricLine(string label, string initial)
    {
        var border = new Border { Background = BrushFrom("#050505"), CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 4, 0, 4) };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var name = Heading(label, 11);
        var value = Body(initial);
        Grid.SetColumn(value, 1);
        grid.Children.Add(name);
        grid.Children.Add(value);
        border.Child = grid;
        return (border, value);
    }

    private UIElement BuildBackup()
    {
        var root = PageContainer();
        root.Children.Add(PageIntro("Backup", "Crie uma cópia local de informações importantes e um ponto de restauração do Windows."));
        root.Children.Add(ActionCard("Criar backup local", "Salva inventário do hardware, plano de energia atual e configuração de rede em C:\\ynoez\\Backups. Não altera o sistema.", "Criar backup", async () =>
        {
            var folder = await BackupService.CreateLocalSnapshotAsync();
            MessageBox.Show("Backup criado em:\n" + folder, "ynoez", MessageBoxButton.OK, MessageBoxImage.Information);
        }, "SEGURO"));
        root.Children.Add(ActionCard("Criar ponto de restauração", "Solicita ao Windows um ponto de restauração. Pode exigir elevação e a Proteção do Sistema precisa estar ativada.", "Criar ponto", async () =>
        {
            if (MessageBox.Show("Criar um ponto de restauração do Windows agora?", "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            var result = await BackupService.CreateRestorePointAsync();
            MessageBox.Show(result, "ynoez", MessageBoxButton.OK, MessageBoxImage.Information);
        }, "WINDOWS"));
        root.Children.Add(NoteCard("Antes de qualquer otimização", "O snapshot local não substitui uma imagem completa do disco. Para alterações de BIOS, firmware ou partições, faça também um backup externo e siga as instruções oficiais do fabricante."));
        return root;
    }

    private UIElement BuildTweaks()
    {
        var root = PageContainer();
        root.Children.Add(PageIntro("Otimização", "Aplique um perfil de jogo reversível ou deixe o ynoez ajustar a energia automaticamente."));

        var gameCard = MakeCard();
        var gamePanel = new StackPanel { Margin = new Thickness(20, 18, 20, 18) };
        gamePanel.Children.Add(Heading("Otimização para jogos", 16));
        gamePanel.Children.Add(Body("Reduz a captura em segundo plano do Xbox, diminui efeitos visuais do Windows e seleciona Alto desempenho quando nenhum outro perfil de energia do ynoez já controla o plano. Cada alteração é salva para restauração."));
        var gameStatus = Body(QuickGameOptimizationService.IsApplied
            ? "Perfil aplicado. As alterações estão registradas para restauração."
            : "Nenhuma otimização rápida aplicada.");
        gameStatus.Margin = new Thickness(0, 9, 0, 10);
        var gameButtons = new WrapPanel();
        var applyGame = new Button
        {
            Content = "Otimizar agora",
            Style = (Style)FindResource("AccentButton"),
            Margin = new Thickness(0, 0, 8, 0)
        };
        var restoreGame = new Button
        {
            Content = "Restaurar alterações",
            Style = (Style)FindResource("QuietButton"),
            IsEnabled = QuickGameOptimizationService.IsApplied
        };
        applyGame.IsEnabled = !QuickGameOptimizationService.IsApplied;
        applyGame.Click += async (_, _) =>
        {
            if (_autoProfileCts is not null)
            {
                gameStatus.Text = "Pause os perfis automáticos antes de aplicar o perfil rápido.";
                return;
            }
            if (MessageBox.Show(
                "Aplicar o perfil para jogos? A captura em segundo plano do Xbox será desativada, alguns efeitos visuais serão reduzidos e o plano de energia poderá mudar para Alto desempenho. NVIDIA Instant Replay, OBS, drivers e anti-cheats não serão alterados.",
                "Confirmar otimização", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            applyGame.IsEnabled = false;
            restoreGame.IsEnabled = false;
            gameStatus.Text = "Aplicando e verificando as configurações…";
            try
            {
                gameStatus.Text = await Task.Run(QuickGameOptimizationService.Apply);
                restoreGame.IsEnabled = QuickGameOptimizationService.IsApplied;
            }
            catch (Exception ex)
            {
                AppLog.Write("Otimização rápida para jogos falhou: " + ex);
                gameStatus.Text = "A aplicação foi interrompida: " + ex.Message;
                restoreGame.IsEnabled = QuickGameOptimizationService.IsApplied;
            }
            finally
            {
                applyGame.IsEnabled = !QuickGameOptimizationService.IsApplied;
            }
        };
        restoreGame.Click += async (_, _) =>
        {
            restoreGame.IsEnabled = false;
            applyGame.IsEnabled = false;
            gameStatus.Text = "Restaurando valores anteriores…";
            try
            {
                gameStatus.Text = await Task.Run(QuickGameOptimizationService.Restore);
            }
            catch (Exception ex)
            {
                AppLog.Write("Restauração da otimização rápida falhou: " + ex);
                gameStatus.Text = "A restauração precisa de atenção: " + ex.Message;
            }
            finally
            {
                restoreGame.IsEnabled = QuickGameOptimizationService.IsApplied;
                applyGame.IsEnabled = !QuickGameOptimizationService.IsApplied;
            }
        };
        gameButtons.Children.Add(applyGame);
        gameButtons.Children.Add(restoreGame);
        gamePanel.Children.Add(gameStatus);
        gamePanel.Children.Add(gameButtons);
        gameCard.Child = gamePanel;
        root.Children.Add(gameCard);

        var focusCard = MakeCard();
        var focusPanel = new StackPanel { Margin = new Thickness(20, 15, 20, 15) };
        focusPanel.Children.Add(Heading("Prioridade por aplicativo", 14));
        focusPanel.Children.Add(Body("Dê prioridade ao CS2 e reduza CPU/memória das instâncias AFK do Roblox. Uma entrada controla todas as instâncias com o mesmo executável."));
        focusPanel.Children.Add(SmallAction("Configurar Foco de Jogo", () => ShowPage("Foco de Jogo")));
        focusCard.Child = focusPanel;
        root.Children.Add(focusCard);

        var autoCard = MakeCard();
        var autoPanel = new StackPanel { Margin = new Thickness(20, 15, 20, 15) };
        autoPanel.Children.Add(Heading("Perfis automáticos", 14));
        autoPanel.Children.Add(Body("Jogo em uso: Performance. Jogo ou automação protegida em segundo plano: Equilibrado. Sem tarefas protegidas e após 15 minutos sem atividade: economia."));
        var autoStatus = Body(_autoProfileCts is null ? "Automação desligada." : "Automação ativa.");
        autoStatus.Margin = new Thickness(0, 8, 0, 8);
        var autoButtons = new WrapPanel();
        var startAuto = new Button
        {
            Content = "Ativar automático",
            Style = (Style)FindResource("AccentButton"),
            IsEnabled = _autoProfileCts is null,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var stopAuto = new Button
        {
            Content = "Parar",
            Style = (Style)FindResource("QuietButton"),
            IsEnabled = _autoProfileCts is not null
        };
        startAuto.Click += async (_, _) =>
        {
            if (_autoProfileCts is not null)
                return;
            if (QuickGameOptimizationService.IsApplied)
            {
                MessageBox.Show("Restaure o perfil rápido de jogos antes de ativar a troca automática de energia.",
                    "Perfis automáticos", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (MessageBox.Show("Ativar a troca automática de energia conforme jogos, automações protegidas e inatividade?",
                "Perfis automáticos", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            _autoProfileCts = new CancellationTokenSource();
            var token = _autoProfileCts.Token;
            startAuto.IsEnabled = false;
            stopAuto.IsEnabled = true;
            autoStatus.Text = "Inicializando automação…";
            try
            {
                await AutoProfileService.RunAsync(status =>
                {
                    if (!Dispatcher.HasShutdownStarted)
                        Dispatcher.Invoke(() => autoStatus.Text =
                            "Modo: " + AutoProfileService.GetModeDisplayName(status.Mode) +
                            " • Inatividade: " + status.IdleTime.TotalMinutes.ToString("0") + " min" +
                            (status.ActiveGame.Length > 0 ? " • Jogo: " + status.ActiveGame : "") +
                            (status.ActiveProtectedProcess.Length > 0 ? " • Protegido: " + status.ActiveProtectedProcess : ""));
                }, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                autoStatus.Text = "Automação parada. O plano anterior foi restaurado.";
            }
            catch (Exception ex)
            {
                AppLog.Write("Perfis automáticos falharam: " + ex);
                autoStatus.Text = "Falha: " + ex.Message;
            }
            finally
            {
                _autoProfileCts?.Dispose();
                _autoProfileCts = null;
                startAuto.IsEnabled = true;
                stopAuto.IsEnabled = false;
            }
        };
        stopAuto.Click += (_, _) => _autoProfileCts?.Cancel();
        autoButtons.Children.Add(startAuto);
        autoButtons.Children.Add(stopAuto);
        autoPanel.Children.Add(autoStatus);
        autoPanel.Children.Add(autoButtons);
        autoCard.Child = autoPanel;
        root.Children.Add(autoCard);

        return root;
    }

    private UIElement BuildNetwork()
    {
        var root = PageContainer();
        root.Children.Add(PageIntro("Network", "Mede latência, jitter, perda de pacotes, DNS, conectividade TCP e rota."));
        var resultCard = MakeCard();
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(Heading("Diagnóstico de conexão", 15));
        panel.Children.Add(Body("O teste mede ICMP no gateway e em 1.1.1.1, testa HTTPS/TCP em dois destinos, resolve DNS IPv4/IPv6 e rastreia a rota com tracert. Nenhuma configuração de rede será modificada."));
        var output = new TextBlock
        {
            Text = "Pronto para testar.", Foreground = SecondaryBrush, FontFamily = new FontFamily("Consolas"),
            FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 15, 0, 10)
        };
        var progressText = new TextBlock { Text = "", Foreground = AccentBrush, FontSize = 10, Margin = new Thickness(0, 0, 0, 10) };
        var diagnosticButtons = new WrapPanel();
        var run = new Button { Content = "Executar diagnóstico", Style = (Style)FindResource("AccentButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 8, 0) };
        var cancelDiagnostic = new Button { Content = "Cancelar", Style = (Style)FindResource("QuietButton"), IsEnabled = false };
        run.Click += async (_, _) =>
        {
            if (_networkDiagnosticCts is not null) return;
            _networkDiagnosticCts = new CancellationTokenSource();
            var token = _networkDiagnosticCts.Token;
            run.IsEnabled = false;
            cancelDiagnostic.IsEnabled = true;
            output.Text = "Executando…";
            progressText.Text = "Preparando medições";
            var progress = new Progress<string>(s => progressText.Text = s);
            try { output.Text = await NetworkDiagnosticService.RunAsync(progress, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                if (IsLoaded) output.Text = "Diagnóstico cancelado. Nenhuma configuração de rede foi alterada.";
            }
            catch (Exception ex)
            {
                output.Text = "Falha no diagnóstico: " + ex.Message;
                AppLog.Write("Diagnóstico de rede: " + ex);
            }
            finally
            {
                _networkDiagnosticCts?.Dispose();
                _networkDiagnosticCts = null;
                run.IsEnabled = true;
                cancelDiagnostic.IsEnabled = false;
                if (IsLoaded) progressText.Text = "Concluído";
            }
        };
        cancelDiagnostic.Click += (_, _) => _networkDiagnosticCts?.Cancel();
        diagnosticButtons.Children.Add(run);
        diagnosticButtons.Children.Add(cancelDiagnostic);
        panel.Children.Add(progressText);
        panel.Children.Add(output);
        panel.Children.Add(diagnosticButtons);
        resultCard.Child = panel;
        root.Children.Add(resultCard);

        var monitorCard = MakeCard();
        var monitorPanel = new StackPanel { Margin = new Thickness(20) };
        monitorPanel.Children.Add(Heading("Monitoramento contínuo", 15));
        monitorPanel.Children.Add(Body("Registra a cada 5 segundos os gateways IPv4/IPv6 quando reportados, dois destinos públicos (Cloudflare e Google) por ICMP e TCP/443 em IPv4 e IPv6, além de resolução DNS e contagem de respostas A/AAAA. A janela estatística móvel usa os últimos 60 segundos e gera uma hipótese de causa sem alterar a rede. O CSV fica em C:\\ynoez\\Logs. Se fechar a janela com o monitor ativo, o ynoez fica na bandeja e continua coletando; duplo clique no ícone reabre a janela e o menu contextual permite encerrar o processo. A coleta pausa enquanto o Windows estiver em suspensão/hibernação ou o computador estiver desligado."));
        var monitorStatus = new TextBlock { Text = "Monitor parado.", Foreground = SecondaryBrush, FontSize = 11, Margin = new Thickness(0, 10, 0, 10), TextWrapping = TextWrapping.Wrap };
        var monitorButtons = new WrapPanel();
        var startMonitor = new Button { Content = "Iniciar monitor (5 s)", Style = (Style)FindResource("AccentButton"), MinWidth = 150, Margin = new Thickness(0, 0, 8, 0) };
        var stopMonitor = new Button { Content = "Parar monitor", Style = (Style)FindResource("QuietButton"), MinWidth = 120, IsEnabled = false };
        startMonitor.Click += async (_, _) =>
        {
            if (_networkMonitorCts is not null) return;
            _networkMonitorCts = new CancellationTokenSource();
            var token = _networkMonitorCts.Token;
            startMonitor.IsEnabled = false;
            stopMonitor.IsEnabled = true;
            monitorStatus.Text = "Monitorando gateways IPv4/IPv6, Cloudflare, Google e DNS…";
            try
            {
                var logFile = await NetworkMonitorService.RunAsync(TimeSpan.FromSeconds(5), sample =>
                {
                    var assessment = NetworkRootCauseService.Analyze(sample);
                    monitorStatus.Text = sample.Timestamp.ToString("HH:mm:ss") +
                        " | Gateway " + (string.IsNullOrWhiteSpace(sample.Gateway) ? "—" : sample.Gateway) +
                        ": " + sample.GatewayStatus + (sample.GatewayMilliseconds.HasValue ? " " + sample.GatewayMilliseconds + " ms" : "") +
                        " | 1.1.1.1 ICMP/TCP: " + sample.ExternalStatus + "/" + sample.ExternalTcpStatus +
                        " | 8.8.8.8 ICMP/TCP: " + sample.SecondaryExternalStatus + "/" + sample.SecondaryExternalTcpStatus +
                        " | DNS: " + sample.DnsStatus + (sample.DnsMilliseconds.HasValue ? " " + sample.DnsMilliseconds.Value.ToString("0.0") + " ms" : "") +
                        " | Respostas DNS A/AAAA: " + sample.DnsIpv4AddressCount + "/" + sample.DnsIpv6AddressCount +
                        " | IPv6 GW: " + (string.IsNullOrWhiteSpace(sample.Ipv6Gateway) ? "—" : sample.Ipv6Gateway) +
                        " " + sample.Ipv6GatewayStatus +
                        " | IPv6 CF ICMP/TCP: " + sample.Ipv6ExternalStatus + "/" + sample.Ipv6ExternalTcpStatus +
                        " | IPv6 Google ICMP/TCP: " + sample.Ipv6SecondaryExternalStatus + "/" + sample.Ipv6SecondaryExternalTcpStatus +
                        " | Perda 60s IPv6 GW/CF/Google: " + sample.Ipv6GatewayLoss60sPercent.ToString("0") + "/" +
                            sample.Ipv6ExternalLoss60sPercent.ToString("0") + "/" + sample.Ipv6SecondaryExternalLoss60sPercent.ToString("0") + "%" +
                        " | Falha TCP IPv6 CF/Google: " + sample.Ipv6ExternalTcpFailure60sPercent.ToString("0") + "/" +
                            sample.Ipv6SecondaryExternalTcpFailure60sPercent.ToString("0") + "%" +
                        " | Perda 60s GW/CF/Google: " + sample.GatewayLoss60sPercent.ToString("0") + "/" +
                            sample.ExternalLoss60sPercent.ToString("0") + "/" + sample.SecondaryExternalLoss60sPercent.ToString("0") + "%" +
                        " | Falha TCP CF/Google: " + sample.ExternalTcpFailure60sPercent.ToString("0") + "/" +
                            sample.SecondaryExternalTcpFailure60sPercent.ToString("0") + "%" +
                        " | Falha DNS: " + sample.DnsFailure60sPercent.ToString("0") + "%" +
                        " | Jitter GW/ICMP/TCP: " +
                            (sample.GatewayJitter60sMilliseconds.HasValue ? sample.GatewayJitter60sMilliseconds.Value.ToString("0.0") : "—") + "/" +
                            (sample.ExternalJitter60sMilliseconds.HasValue ? sample.ExternalJitter60sMilliseconds.Value.ToString("0.0") : "—") + "/" +
                            (sample.ExternalTcpJitter60sMilliseconds.HasValue ? sample.ExternalTcpJitter60sMilliseconds.Value.ToString("0.0") : "—") + " ms" +
                        "\n" + assessment.Category + ": " + assessment.Assessment;
                }, token);
                monitorStatus.Text = "Monitor encerrado. CSV salvo em: " + logFile;
            }
            catch (Exception ex)
            {
                AppLog.Write("Monitoramento de rede falhou: " + ex);
                monitorStatus.Text = "Falha no monitoramento: " + ex.Message;
            }
            finally
            {
                _networkMonitorCts?.Dispose();
                _networkMonitorCts = null;
                startMonitor.IsEnabled = true;
                stopMonitor.IsEnabled = false;
            }
        };
        stopMonitor.Click += (_, _) => _networkMonitorCts?.Cancel();
        monitorButtons.Children.Add(startMonitor);
        monitorButtons.Children.Add(stopMonitor);
        monitorPanel.Children.Add(monitorStatus);
        monitorPanel.Children.Add(monitorButtons);
        monitorCard.Child = monitorPanel;
        root.Children.Add(monitorCard);

        var adapterCard = MakeCard();
        var adapterPanel = new StackPanel { Margin = new Thickness(20) };
        adapterPanel.Children.Add(Heading("Energia dos adaptadores físicos", 15));
        adapterPanel.Children.Add(Body("A consulta é somente leitura e não pede UAC. Se compatível, permite desativar somente Selective Suspend e Device Sleep on Disconnect, salvando os valores originais. A alteração e a restauração exigem UAC, não reiniciam o adaptador automaticamente e podem exigir reconexão/reinício para aplicar. Pode aumentar consumo e não garante corrigir quedas."));
        var adapterResults = new StackPanel();
        var adapterService = new AdapterPowerManagementService();
        var scanAdapters = new Button { Content = "Analisar adaptadores", Style = (Style)FindResource("AccentButton"), MinWidth = 155, Margin = new Thickness(0, 10, 0, 10) };
        scanAdapters.Click += async (_, _) =>
        {
            scanAdapters.IsEnabled = false;
            adapterResults.Children.Clear();
            adapterResults.Children.Add(Body("Consultando recursos do driver. O Windows pode solicitar UAC…"));
            try
            {
                var adapters = await Task.Run(() => adapterService.GetPhysicalAdapters());
                adapterResults.Children.Clear();
                if (adapters.Count == 0)
                    adapterResults.Children.Add(NoteCard("Nenhum adaptador compatível", "O Windows não retornou adaptadores físicos com dados de gerenciamento de energia."));
                foreach (var adapter in adapters)
                {
                    var details = adapter.InterfaceDescription + "\nEstado: " + adapter.Status + " | Link: " + adapter.LinkSpeed +
                        "\nSelective Suspend: " + adapter.SelectiveSuspend + " | Device Sleep on Disconnect: " + adapter.DeviceSleepOnDisconnect;
                    if (adapterService.CanRestore(adapter.Name))
                    {
                        adapterResults.Children.Add(ActionCard(adapter.Name, details, "Restaurar energia original", async () =>
                        {
                            if (MessageBox.Show("Restaurar as opções originais de energia de \"" + adapter.Name + "\"?", "Confirmar restauração",
                                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                            await Task.Run(() => adapterService.Restore(adapter.Name));
                            MessageBox.Show("Opções originais restauradas e verificadas.", "ynoez", MessageBoxButton.OK, MessageBoxImage.Information);
                            ShowPage("Network");
                        }, "BACKUP"));
                    }
                    else if (adapterService.CanReducePowerSaving(adapter))
                    {
                        adapterResults.Children.Add(ActionCard(adapter.Name, details, "Reduzir economia", async () =>
                        {
                            if (MessageBox.Show("Desativar apenas as opções de economia compatíveis neste adaptador? A alteração pode aumentar o consumo e pode exigir reconexão/reinício. O estado original será salvo.",
                                "Confirmar adaptador", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                            await Task.Run(() => adapterService.ReducePowerSaving(adapter));
                            MessageBox.Show("Opções selecionadas desativadas e verificadas. Backup original salvo.", "ynoez", MessageBoxButton.OK, MessageBoxImage.Information);
                            ShowPage("Network");
                        }, "REVERSÍVEL"));
                    }
                    else
                        adapterResults.Children.Add(NoteCard(adapter.Name + " — sem ajuste necessário", details + "\nNenhuma das opções suportadas está habilitada."));
                }
            }
            catch (Exception ex)
            {
                AppLog.Write("Consulta de energia dos adaptadores falhou: " + ex);
                adapterResults.Children.Clear();
                adapterResults.Children.Add(NoteCard("Não foi possível consultar", ex.Message));
            }
            finally { scanAdapters.IsEnabled = true; }
        };
        adapterPanel.Children.Add(scanAdapters);
        adapterPanel.Children.Add(adapterResults);
        adapterCard.Child = adapterPanel;
        root.Children.Add(adapterCard);

        var inventoryCard = MakeCard();
        var inventoryPanel = new StackPanel { Margin = new Thickness(20) };
        inventoryPanel.Children.Add(Heading("Inventário técnico dos adaptadores", 15));
        inventoryPanel.Children.Add(Body("Consulta somente leitura das interfaces de rede, endereços, gateways, DNS, MTU, DHCP e informações do driver quando o Windows as fornece. Não altera propriedades, reinicia adaptadores ou substitui o diagnóstico do roteador/provedor."));
        var inventoryStatus = Body("Inventário ainda não consultado.");
        var inventoryGrid = new DataGrid
        {
            AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false,
            CanUserDeleteRows = false, CanUserResizeRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            Background = BrushFrom("#050505"), Foreground = PrimaryBrush,
            RowBackground = BrushFrom("#050505"), AlternatingRowBackground = BrushFrom("#171717"),
            BorderBrush = BorderBrushSoft, Height = 270, Margin = new Thickness(0, 10, 0, 10)
        };
        inventoryGrid.Columns.Add(new DataGridTextColumn { Header = "Adaptador", Binding = new Binding(nameof(NetworkAdapterInventory.Name)), Width = new DataGridLength(1.4, DataGridLengthUnitType.Star) });
        inventoryGrid.Columns.Add(new DataGridTextColumn { Header = "Tipo", Binding = new Binding(nameof(NetworkAdapterInventory.Type)), Width = new DataGridLength(85) });
        inventoryGrid.Columns.Add(new DataGridTextColumn { Header = "Estado", Binding = new Binding(nameof(NetworkAdapterInventory.Status)), Width = new DataGridLength(70) });
        inventoryGrid.Columns.Add(new DataGridTextColumn { Header = "Link", Binding = new Binding(nameof(NetworkAdapterInventory.LinkSpeedDisplay)), Width = new DataGridLength(85) });
        inventoryGrid.Columns.Add(new DataGridTextColumn { Header = "IPv4/IPv6", Binding = new Binding(nameof(NetworkAdapterInventory.IpAddresses)), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        inventoryGrid.Columns.Add(new DataGridTextColumn { Header = "Gateway", Binding = new Binding(nameof(NetworkAdapterInventory.Gateways)), Width = new DataGridLength(1.2, DataGridLengthUnitType.Star) });
        inventoryGrid.Columns.Add(new DataGridTextColumn { Header = "DNS", Binding = new Binding(nameof(NetworkAdapterInventory.DnsServers)), Width = new DataGridLength(1.2, DataGridLengthUnitType.Star) });
        inventoryGrid.Columns.Add(new DataGridTextColumn { Header = "MTU v4/v6", Binding = new Binding(nameof(NetworkAdapterInventory.MtuDisplay)), Width = new DataGridLength(90) });
        inventoryGrid.Columns.Add(new DataGridTextColumn { Header = "Driver", Binding = new Binding(nameof(NetworkAdapterInventory.DriverVersion)), Width = new DataGridLength(100) });
        var inventoryButtons = new WrapPanel();
        var refreshInventory = new Button { Content = "Analisar interfaces", Style = (Style)FindResource("AccentButton"), Margin = new Thickness(0, 0, 8, 0) };
        var exportInventory = new Button { Content = "Exportar CSV", Style = (Style)FindResource("QuietButton"), IsEnabled = false };
        IReadOnlyList<NetworkAdapterInventory> inventory = Array.Empty<NetworkAdapterInventory>();
        refreshInventory.Click += async (_, _) =>
        {
            refreshInventory.IsEnabled = false;
            exportInventory.IsEnabled = false;
            inventoryStatus.Text = "Consultando interfaces e drivers pelo Windows…";
            try
            {
                inventory = await Task.Run(NetworkAdapterInventoryService.Capture);
                inventoryGrid.ItemsSource = inventory;
                inventoryStatus.Text = inventory.Count + " interfaces consultadas • " +
                    inventory.Count(x => string.Equals(x.Status, "Up", StringComparison.OrdinalIgnoreCase)) +
                    " ativas • leitura somente de propriedades.";
                exportInventory.IsEnabled = inventory.Count > 0;
            }
            catch (Exception ex)
            {
                AppLog.Write("Inventário técnico de rede falhou: " + ex);
                inventoryStatus.Text = "Não foi possível consultar as interfaces: " + ex.Message;
            }
            finally { refreshInventory.IsEnabled = true; }
        };
        exportInventory.Click += (_, _) =>
        {
            if (inventory.Count == 0) return;
            var path = NetworkAdapterInventoryService.ExportCsv(inventory);
            inventoryStatus.Text = "Relatório CSV salvo em: " + path;
        };
        inventoryButtons.Children.Add(refreshInventory);
        inventoryButtons.Children.Add(exportInventory);
        inventoryPanel.Children.Add(inventoryStatus);
        inventoryPanel.Children.Add(inventoryGrid);
        inventoryPanel.Children.Add(inventoryButtons);
        inventoryPanel.Children.Add(NoteCard("Leitura de MTU e driver", "MTU e versão de driver dependem do que o Windows/WMI expõe para cada interface. Adaptadores virtuais podem aparecer no inventário; não os desative por nome sem confirmar qual VPN, hipervisor ou software os utiliza."));
        inventoryCard.Child = inventoryPanel;
        root.Children.Add(inventoryCard);

        var advancedCard = MakeCard();
        var advancedPanel = new StackPanel { Margin = new Thickness(20) };
        advancedPanel.Children.Add(Heading("Propriedades avançadas do driver (somente leitura)", 15));
        advancedPanel.Children.Add(Body("Consulta os parâmetros que o driver realmente expõe para adaptadores físicos, como EEE, Interrupt Moderation, RSS, LSO, Checksum Offload, Flow Control, Speed & Duplex e buffers. Não altera valores, não reinicia a placa e não solicita UAC."));
        var advancedStatus = Body("Propriedades avançadas ainda não consultadas.");
        var advancedGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserResizeRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            Background = BrushFrom("#050505"),
            Foreground = PrimaryBrush,
            RowBackground = BrushFrom("#050505"),
            AlternatingRowBackground = BrushFrom("#171717"),
            BorderBrush = BorderBrushSoft,
            Height = 330,
            Margin = new Thickness(0, 10, 0, 10)
        };
        advancedGrid.Columns.Add(new DataGridTextColumn { Header = "Adaptador", Binding = new Binding(nameof(NetworkAdapterAdvancedProperty.AdapterName)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        advancedGrid.Columns.Add(new DataGridTextColumn { Header = "Propriedade", Binding = new Binding(nameof(NetworkAdapterAdvancedProperty.DisplayName)), Width = new DataGridLength(1.25, DataGridLengthUnitType.Star) });
        advancedGrid.Columns.Add(new DataGridTextColumn { Header = "Valor atual", Binding = new Binding(nameof(NetworkAdapterAdvancedProperty.DisplayValue)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        advancedGrid.Columns.Add(new DataGridTextColumn { Header = "Categoria", Binding = new Binding(nameof(NetworkAdapterAdvancedProperty.Category)), Width = new DataGridLength(0.9, DataGridLengthUnitType.Star) });
        advancedGrid.Columns.Add(new DataGridTextColumn { Header = "Avaliação", Binding = new Binding(nameof(NetworkAdapterAdvancedProperty.Assessment)), Width = new DataGridLength(1.25, DataGridLengthUnitType.Star) });
        var guidanceStyle = new Style(typeof(TextBlock));
        guidanceStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        guidanceStyle.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        advancedGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Orientação",
            Binding = new Binding(nameof(NetworkAdapterAdvancedProperty.Guidance)),
            ElementStyle = guidanceStyle,
            Width = new DataGridLength(2.4, DataGridLengthUnitType.Star)
        });
        var advancedButtons = new WrapPanel();
        var refreshAdvanced = new Button { Content = "Analisar propriedades", Style = (Style)FindResource("AccentButton"), Margin = new Thickness(0, 0, 8, 0) };
        var exportAdvanced = new Button { Content = "Exportar CSV", Style = (Style)FindResource("QuietButton"), IsEnabled = false };
        NetworkAdapterAdvancedDiagnosticsSnapshot? advancedSnapshot = null;
        refreshAdvanced.Click += async (_, _) =>
        {
            refreshAdvanced.IsEnabled = false;
            exportAdvanced.IsEnabled = false;
            advancedStatus.Text = "Lendo propriedades publicadas pelos drivers…";
            try
            {
                advancedSnapshot = await NetworkAdapterAdvancedDiagnosticsService.CaptureAsync();
                advancedGrid.ItemsSource = advancedSnapshot.Properties;
                advancedStatus.Text = advancedSnapshot.Properties.Count + " propriedades lidas às " +
                    advancedSnapshot.CapturedAt.ToLocalTime().ToString("HH:mm:ss") +
                    " • consulta somente leitura.";
                if (advancedSnapshot.Errors.Count > 0)
                    advancedStatus.Text += " • " + advancedSnapshot.Errors.Count + " aviso(s): " +
                        string.Join(" | ", advancedSnapshot.Errors.Take(2));
                exportAdvanced.IsEnabled = advancedSnapshot.Properties.Count > 0;
                if (advancedSnapshot.Properties.Count == 0 && advancedSnapshot.Errors.Count == 0)
                    advancedStatus.Text += " O driver não publicou propriedades avançadas para os adaptadores físicos detectados.";
            }
            catch (Exception ex)
            {
                AppLog.Write("Auditoria de propriedades avançadas de rede falhou: " + ex);
                advancedStatus.Text = "Não foi possível consultar os drivers: " + ex.Message;
            }
            finally { refreshAdvanced.IsEnabled = true; }
        };
        exportAdvanced.Click += (_, _) =>
        {
            if (advancedSnapshot is null || advancedSnapshot.Properties.Count == 0) return;
            var path = NetworkAdapterAdvancedDiagnosticsService.ExportCsv(advancedSnapshot);
            advancedStatus.Text = "Relatório CSV salvo em: " + path;
        };
        advancedButtons.Children.Add(refreshAdvanced);
        advancedButtons.Children.Add(exportAdvanced);
        advancedPanel.Children.Add(advancedStatus);
        advancedPanel.Children.Add(advancedGrid);
        advancedPanel.Children.Add(advancedButtons);
        advancedPanel.Children.Add(NoteCard("Não existe tweak universal de rede", "EEE e Interrupt Moderation podem mudar consumo, CPU e latência; RSS e offloads normalmente devem seguir o suporte do driver; Speed & Duplex costuma ficar em negociação automática. Essas são orientações condicionais, não comandos de otimização. O ynoez não muda nenhum desses valores e não promete corrigir quedas causadas pelo roteador ou provedor."));
        advancedCard.Child = advancedPanel;
        root.Children.Add(advancedCard);

        var statsCard = MakeCard();
        var statsPanel = new StackPanel { Margin = new Thickness(20) };
        statsPanel.Children.Add(Heading("Contadores dos adaptadores", 15));
        statsPanel.Children.Add(Body("Leitura local sem UAC dos bytes, pacotes, erros e descartes acumulados por interface. Esses contadores normalmente zeram quando o adaptador é reiniciado ou o Windows reinicia; use duas leituras em momentos diferentes para comparar. Contadores acumulados não identificam sozinhos a causa de uma queda."));
        var statsStatus = Body("Contadores ainda não consultados.");
        var statsGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserResizeRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            Background = BrushFrom("#050505"),
            Foreground = PrimaryBrush,
            RowBackground = BrushFrom("#050505"),
            AlternatingRowBackground = BrushFrom("#171717"),
            BorderBrush = BorderBrushSoft,
            Height = 240,
            Margin = new Thickness(0, 10, 0, 10)
        };
        statsGrid.Columns.Add(new DataGridTextColumn { Header = "Adaptador", Binding = new Binding(nameof(NetworkAdapterStatistics.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        statsGrid.Columns.Add(new DataGridTextColumn { Header = "Estado", Binding = new Binding(nameof(NetworkAdapterStatistics.Status)), Width = new DataGridLength(75) });
        statsGrid.Columns.Add(new DataGridTextColumn { Header = "Link", Binding = new Binding(nameof(NetworkAdapterStatistics.LinkSpeedDisplay)), Width = new DataGridLength(85) });
        statsGrid.Columns.Add(new DataGridTextColumn { Header = "Recebido", Binding = new Binding(nameof(NetworkAdapterStatistics.BytesReceivedDisplay)), Width = new DataGridLength(85) });
        statsGrid.Columns.Add(new DataGridTextColumn { Header = "Enviado", Binding = new Binding(nameof(NetworkAdapterStatistics.BytesSentDisplay)), Width = new DataGridLength(85) });
        statsGrid.Columns.Add(new DataGridTextColumn { Header = "Erros RX", Binding = new Binding(nameof(NetworkAdapterStatistics.ReceiveErrors)), Width = new DataGridLength(70) });
        statsGrid.Columns.Add(new DataGridTextColumn { Header = "Erros TX", Binding = new Binding(nameof(NetworkAdapterStatistics.SendErrors)), Width = new DataGridLength(70) });
        statsGrid.Columns.Add(new DataGridTextColumn { Header = "Descartes RX", Binding = new Binding(nameof(NetworkAdapterStatistics.ReceiveDiscardsDisplay)), Width = new DataGridLength(85) });
        statsGrid.Columns.Add(new DataGridTextColumn { Header = "Descartes TX", Binding = new Binding(nameof(NetworkAdapterStatistics.SendDiscardsDisplay)), Width = new DataGridLength(85) });
        statsGrid.Columns.Add(new DataGridTextColumn { Header = "Problemas RX", Binding = new Binding(nameof(NetworkAdapterStatistics.ReceiveProblemPercentDisplay)), Width = new DataGridLength(90) });
        statsGrid.Columns.Add(new DataGridTextColumn { Header = "Problemas TX", Binding = new Binding(nameof(NetworkAdapterStatistics.SendProblemPercentDisplay)), Width = new DataGridLength(90) });
        var refreshStats = new Button { Content = "Atualizar contadores", Style = (Style)FindResource("AccentButton"), HorizontalAlignment = HorizontalAlignment.Left };
        refreshStats.Click += async (_, _) =>
        {
            refreshStats.IsEnabled = false;
            statsStatus.Text = "Lendo contadores dos adaptadores…";
            try
            {
                var samples = await Task.Run(NetworkAdapterStatisticsService.Capture);
                statsGrid.ItemsSource = samples;
                statsStatus.Text = samples.Count + " interfaces consultadas às " +
                    (samples.Count > 0 ? samples[0].CapturedAt.ToString("HH:mm:ss") : DateTime.Now.ToString("HH:mm:ss")) +
                    ". Erros e descartes são contadores cumulativos do Windows.";
                if (samples.Count == 0)
                    statsStatus.Text += " Nenhuma interface retornou estatísticas.";
            }
            catch (Exception ex)
            {
                AppLog.Write("Contadores de rede falharam: " + ex);
                statsStatus.Text = "Falha ao consultar contadores: " + ex.Message;
            }
            finally { refreshStats.IsEnabled = true; }
        };
        statsPanel.Children.Add(statsStatus);
        statsPanel.Children.Add(statsGrid);
        statsPanel.Children.Add(refreshStats);
        statsCard.Child = statsPanel;
        root.Children.Add(statsCard);

        root.Children.Add(NoteCard("Como interpretar", "Perda também no gateway sugere um problema dentro da rede local (Wi-Fi, cabo, adaptador ou roteador). Perda somente no destino externo pode estar na rota ou no provedor. Um teste curto não prova sozinho a causa de desconexões intermitentes."));
        return root;
    }

    private UIElement BuildDebloat()
    {
        var root = PageContainer();
        root.Children.Add(PageIntro("Debloat", "Limpe arquivos e remova aplicativos opcionais sem tocar nos componentes essenciais do Windows."));

        var cleanupCard = MakeCard();
        var cleanupPanel = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
        cleanupPanel.Children.Add(Heading("Limpeza rápida", 15));
        var cleanupStatus = Body("Temporários e caches selecionados; arquivos em uso serão ignorados.");
        cleanupStatus.Margin = new Thickness(0, 6, 0, 10);
        var cleanupButtons = new WrapPanel();
        var cleanTemp = new Button
        {
            Content = "Limpar temporários",
            Style = (Style)FindResource("AccentButton"),
            Margin = new Thickness(0, 0, 8, 0)
        };
        var cleanCaches = new Button
        {
            Content = "Limpar caches Brave + Discord",
            Style = (Style)FindResource("QuietButton")
        };
        cleanTemp.Click += async (_, _) =>
        {
            cleanTemp.IsEnabled = false;
            cleanCaches.IsEnabled = false;
            cleanupStatus.Text = "Limpando arquivos temporários…";
            try
            {
                var result = await CleanupService.ClearWindowsTempAsync();
                cleanupStatus.Text = "Temporários: " + result.Summary;
            }
            catch (Exception ex)
            {
                cleanupStatus.Text = "A limpeza falhou: " + ex.Message;
                AppLog.Write("Limpeza de temporários falhou: " + ex);
            }
            finally
            {
                cleanTemp.IsEnabled = true;
                cleanCaches.IsEnabled = true;
            }
        };
        cleanCaches.Click += async (_, _) =>
        {
            cleanTemp.IsEnabled = false;
            cleanCaches.IsEnabled = false;
            cleanupStatus.Text = "Limpando caches dos aplicativos…";
            try
            {
                var results = await Task.WhenAll(
                    CleanupService.ClearBraveCacheAsync(),
                    CleanupService.ClearDiscordCacheAsync());
                cleanupStatus.Text = "Brave: " + results[0].Summary + Environment.NewLine +
                    "Discord: " + results[1].Summary;
            }
            catch (Exception ex)
            {
                cleanupStatus.Text = "A limpeza de cache falhou: " + ex.Message;
                AppLog.Write("Limpeza de caches falhou: " + ex);
            }
            finally
            {
                cleanTemp.IsEnabled = true;
                cleanCaches.IsEnabled = true;
            }
        };
        cleanupButtons.Children.Add(cleanTemp);
        cleanupButtons.Children.Add(cleanCaches);
        cleanupPanel.Children.Add(cleanupStatus);
        cleanupPanel.Children.Add(cleanupButtons);
        cleanupCard.Child = cleanupPanel;
        root.Children.Add(cleanupCard);

        root.Children.Add(BuildAppxDebloatCard());

        var startupManager = new StartupManagerService();
        var startupPlaceholder = CreateDebloatLoadingCard("Inicialização", "Lendo apenas entradas do usuário que podem ser desativadas com segurança…");
        root.Children.Add(startupPlaceholder);
        _ = LoadDebloatStartupAsync(startupPlaceholder, startupManager);

        var serviceManager = new ServiceTweakService();
        var servicePlaceholder = CreateDebloatLoadingCard("Serviços opcionais", "Consultando somente a lista fechada de serviços opcionais…");
        root.Children.Add(servicePlaceholder);
        _ = LoadDebloatServicesAsync(servicePlaceholder, serviceManager);

        var taskManager = new ScheduledTaskTweakService();
        var taskPlaceholder = CreateDebloatLoadingCard("Tarefas opcionais", "Consultando tarefas de telemetria, feedback e mapas…");
        root.Children.Add(taskPlaceholder);
        _ = LoadDebloatTasksAsync(taskPlaceholder, taskManager);

        root.Children.Add(ActionCard(
            "Programas Win32",
            "Abra o desinstalador oficial do Windows para remover programas tradicionais. O ynoez não executa comandos de desinstalação arbitrários.",
            "Abrir programas instalados",
            () => OpenTargetAsync("ms-settings:appsfeatures"),
            "WINDOWS"));

        root.Children.Add(NoteCard(
            "Proteções preservadas",
            "Store, Calculadora, Notepad, Windows Update, Defender, Firewall, drivers, runtimes, NVIDIA, Vanguard e FACEIT não entram na remoção automática."));
        return root;
    }

    private Border BuildAppxDebloatCard()
    {
        var card = MakeCard();
        var panel = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
        panel.Children.Add(Heading("Aplicativos opcionais do Windows", 15));
        panel.Children.Add(Body("O ynoez detecta aplicativos opcionais conhecidos. Tudo vem selecionado; desmarque o que deseja manter. A remoção afeta somente sua conta atual."));
        var status = Body("Clique em Analisar para localizar aplicativos opcionais instalados.");
        status.Margin = new Thickness(0, 8, 0, 8);
        var list = new ListBox
        {
            Height = 166,
            SelectionMode = SelectionMode.Extended,
            Margin = new Thickness(0, 0, 0, 10)
        };
        var buttons = new WrapPanel();
        var analyze = new Button
        {
            Content = "Analisar aplicativos",
            Style = (Style)FindResource("AccentButton"),
            Margin = new Thickness(0, 0, 8, 0)
        };
        var cancel = new Button
        {
            Content = "Cancelar",
            Style = (Style)FindResource("QuietButton"),
            IsEnabled = false,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var remove = new Button
        {
            Content = "Remover selecionados",
            Style = (Style)FindResource("QuietButton"),
            IsEnabled = false
        };

        async Task AnalyzeAsync()
        {
            if (_appxInventoryCts is not null)
                return;

            var cts = new CancellationTokenSource();
            _appxInventoryCts = cts;
            analyze.IsEnabled = false;
            remove.IsEnabled = false;
            cancel.IsEnabled = true;
            status.Text = "Analisando os pacotes do usuário atual…";
            try
            {
                var packages = await AppPackageInventoryService.GetCurrentUserPackagesAsync(cts.Token);
                var candidates = AppxDebloatService.FindCandidates(packages);
                list.Items.Clear();
                foreach (var candidate in candidates)
                {
                    list.Items.Add(new ListBoxItem
                    {
                        Content = candidate.DisplayName + "  •  " + candidate.Version + Environment.NewLine +
                            candidate.Description,
                        Tag = candidate,
                        IsSelected = true
                    });
                }

                status.Text = candidates.Count == 0
                    ? "Nenhum aplicativo da lista segura foi encontrado."
                    : candidates.Count + " aplicativo(s) opcional(is) encontrado(s). Todos estão selecionados para remoção.";
                remove.IsEnabled = candidates.Count > 0;
            }
            catch (OperationCanceledException)
            {
                status.Text = "Análise cancelada.";
            }
            catch (Exception ex)
            {
                AppLog.Write("Análise AppX de debloat falhou: " + ex);
                status.Text = "Não foi possível analisar os aplicativos: " + ex.Message;
            }
            finally
            {
                if (ReferenceEquals(_appxInventoryCts, cts))
                    _appxInventoryCts = null;
                cts.Dispose();
                analyze.IsEnabled = true;
                cancel.IsEnabled = false;
            }
        }

        analyze.Click += async (_, _) => await AnalyzeAsync();
        cancel.Click += (_, _) => _appxInventoryCts?.Cancel();
        remove.Click += async (_, _) =>
        {
            var selected = list.SelectedItems.OfType<ListBoxItem>()
                .Select(item => item.Tag)
                .OfType<AppxRemovalCandidate>()
                .ToArray();
            if (selected.Length == 0)
                return;

            var names = string.Join(Environment.NewLine, selected.Select(item => "• " + item.DisplayName));
            if (MessageBox.Show(
                "Remover estes aplicativos somente da conta atual?" + Environment.NewLine + Environment.NewLine +
                names + Environment.NewLine + Environment.NewLine +
                "Esta ação não tem reversão automática. Os aplicativos podem ser reinstalados pela Microsoft Store quando ainda disponíveis. Nenhum pacote provisionado ou outro perfil será alterado.",
                "Confirmar remoção de aplicativos", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            if (_appxInventoryCts is not null)
                return;
            var cts = new CancellationTokenSource();
            _appxInventoryCts = cts;
            analyze.IsEnabled = false;
            cancel.IsEnabled = true;
            remove.IsEnabled = false;
            var removed = 0;
            var errors = new List<string>();
            try
            {
                foreach (var candidate in selected)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    status.Text = "Removendo " + candidate.DisplayName + "…";
                    try
                    {
                        await AppxDebloatService.RemoveCurrentUserPackageAsync(candidate, cts.Token);
                        removed++;
                    }
                    catch (Exception ex)
                    {
                        errors.Add(candidate.DisplayName + ": " + ex.Message);
                    }
                }

                status.Text = removed + " aplicativo(s) removido(s)." +
                    (errors.Count > 0 ? Environment.NewLine + string.Join(Environment.NewLine, errors.Take(3)) : "");
            }
            catch (OperationCanceledException)
            {
                status.Text = "Operação cancelada. " + removed + " aplicativo(s) removido(s) antes do cancelamento.";
            }
            finally
            {
                if (ReferenceEquals(_appxInventoryCts, cts))
                    _appxInventoryCts = null;
                cts.Dispose();
                analyze.IsEnabled = true;
                cancel.IsEnabled = false;
                remove.IsEnabled = false;
            }

            if (removed > 0)
                await AnalyzeAsync();
        };

        buttons.Children.Add(analyze);
        buttons.Children.Add(cancel);
        buttons.Children.Add(remove);
        panel.Children.Add(status);
        panel.Children.Add(list);
        panel.Children.Add(buttons);
        card.Child = panel;
        return card;
    }

    private Border CreateDebloatLoadingCard(string title, string status)
    {
        var card = MakeCard();
        var panel = new StackPanel { Margin = new Thickness(20, 13, 20, 13) };
        panel.Children.Add(Heading(title, 14));
        panel.Children.Add(Body(status));
        card.Child = panel;
        return card;
    }

    private async Task LoadDebloatStartupAsync(Border placeholder, StartupManagerService manager)
    {
        try
        {
            var snapshot = await Task.Run(() => new
            {
                Active = manager.GetActiveEntries().Where(entry => entry.CanDisable).ToArray(),
                Disabled = manager.GetDisabledEntries().ToArray()
            });

            var panel = new StackPanel { Margin = new Thickness(20, 13, 20, 13) };
            panel.Children.Add(Heading("Inicialização", 14));
            panel.Children.Add(Body(snapshot.Active.Length + " entrada(s) do usuário podem ser desativadas. " +
                snapshot.Disabled.Length + " backup(s) aguardam restauração."));
            var activeList = new ListBox { Height = 105, Margin = new Thickness(0, 8, 0, 7) };
            foreach (var entry in snapshot.Active)
                activeList.Items.Add(new ListBoxItem
                {
                    Content = entry.Name + "  •  " + entry.Scope,
                    Tag = entry
                });
            var disable = new Button
            {
                Content = "Desativar selecionado",
                Style = (Style)FindResource("QuietButton"),
                IsEnabled = false,
                Margin = new Thickness(0, 0, 8, 0)
            };
            activeList.SelectionChanged += (_, _) => disable.IsEnabled = activeList.SelectedItem is ListBoxItem;
            disable.Click += async (_, _) =>
            {
                if (activeList.SelectedItem is not ListBoxItem item || item.Tag is not StartupEntry entry)
                    return;
                if (MessageBox.Show("Desativar " + entry.Name + " da inicialização? O backup original será salvo.",
                    "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;
                disable.IsEnabled = false;
                try
                {
                    await Task.Run(() => manager.Disable(entry));
                    ShowPage("Debloat");
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Não foi possível desativar a entrada: " + ex.Message, "ynoez", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };
            panel.Children.Add(activeList);
            panel.Children.Add(disable);

            if (snapshot.Disabled.Length > 0)
            {
                var disabledList = new ListBox { Height = 85, Margin = new Thickness(0, 9, 0, 7) };
                foreach (var entry in snapshot.Disabled)
                    disabledList.Items.Add(new ListBoxItem { Content = entry.Name, Tag = entry });
                var restore = new Button
                {
                    Content = "Restaurar selecionado",
                    Style = (Style)FindResource("QuietButton"),
                    IsEnabled = false
                };
                disabledList.SelectionChanged += (_, _) => restore.IsEnabled = disabledList.SelectedItem is ListBoxItem;
                restore.Click += async (_, _) =>
                {
                    if (disabledList.SelectedItem is not ListBoxItem item || item.Tag is not DisabledStartupEntry entry)
                        return;
                    restore.IsEnabled = false;
                    try
                    {
                        await Task.Run(() => manager.Restore(entry));
                        ShowPage("Debloat");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Não foi possível restaurar a entrada: " + ex.Message, "ynoez", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                };
                panel.Children.Add(Heading("Desativados pelo ynoez", 11));
                panel.Children.Add(disabledList);
                panel.Children.Add(restore);
            }

            placeholder.Child = panel;
        }
        catch (Exception ex)
        {
            AppLog.Write("Consulta de inicialização no Debloat falhou: " + ex);
            placeholder.Child = new StackPanel
            {
                Margin = new Thickness(20),
                Children =
                {
                    Heading("Inicialização", 14),
                    Body("Não foi possível carregar os itens: " + ex.Message)
                }
            };
        }
    }

    private async Task LoadDebloatServicesAsync(Border placeholder, ServiceTweakService manager)
    {
        try
        {
            var statuses = await Task.Run(manager.GetStatuses);
            var panel = new StackPanel { Margin = new Thickness(20, 13, 20, 13) };
            panel.Children.Add(Heading("Serviços opcionais", 14));
            panel.Children.Add(Body("Somente serviços de telemetria e recursos opcionais conhecidos. Serviços críticos, rede, áudio, Store, Defender, Update e anti-cheats ficam fora da lista."));
            var list = new ListBox { Height = 132, Margin = new Thickness(0, 8, 0, 7) };
            foreach (var status in statuses.Where(item => item.Available || item.DisabledByYnoez))
                list.Items.Add(new ListBoxItem
                {
                    Content = status.Definition.DisplayName + "  •  " +
                        (status.DisabledByYnoez ? "Desativado pelo ynoez" : status.State + " / " + status.StartMode),
                    Tag = status
                });
            var action = new Button
            {
                Content = "Aplicar / restaurar selecionado",
                Style = (Style)FindResource("QuietButton"),
                IsEnabled = false
            };
            list.SelectionChanged += (_, _) => action.IsEnabled = list.SelectedItem is ListBoxItem;
            action.Click += async (_, _) =>
            {
                if (list.SelectedItem is not ListBoxItem item || item.Tag is not OptionalServiceStatus selected)
                    return;
                var restore = selected.DisabledByYnoez;
                var prompt = restore
                    ? "Restaurar o serviço " + selected.Definition.DisplayName + " ao estado anterior?"
                    : "Desativar o serviço opcional " + selected.Definition.DisplayName + "? A função descrita ficará indisponível enquanto estiver desativado.";
                if (MessageBox.Show(prompt, "Confirmar serviço", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;
                action.IsEnabled = false;
                try
                {
                    if (restore)
                        await Task.Run(() => manager.Restore(selected.Definition));
                    else
                        await Task.Run(() => manager.Disable(selected.Definition));
                    ShowPage("Debloat");
                }
                catch (Exception ex)
                {
                    MessageBox.Show("A operação no serviço falhou: " + ex.Message, "ynoez", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };
            panel.Children.Add(list);
            panel.Children.Add(action);
            placeholder.Child = panel;
        }
        catch (Exception ex)
        {
            AppLog.Write("Consulta de serviços opcionais no Debloat falhou: " + ex);
            placeholder.Child = new StackPanel
            {
                Margin = new Thickness(20),
                Children = { Heading("Serviços opcionais", 14), Body("Consulta indisponível: " + ex.Message) }
            };
        }
    }

    private async Task LoadDebloatTasksAsync(Border placeholder, ScheduledTaskTweakService manager)
    {
        try
        {
            var statuses = await Task.Run(manager.GetStatuses);
            var panel = new StackPanel { Margin = new Thickness(20, 13, 20, 13) };
            panel.Children.Add(Heading("Tarefas opcionais", 14));
            panel.Children.Add(Body("Tarefas de telemetria, feedback e mapas. Windows Update, Defender, manutenção e drivers não são alterados."));
            var list = new ListBox { Height = 132, Margin = new Thickness(0, 8, 0, 7) };
            foreach (var status in statuses.Where(item => item.Available || item.DisabledByYnoez))
                list.Items.Add(new ListBoxItem
                {
                    Content = status.Definition.DisplayName + "  •  " +
                        (status.DisabledByYnoez ? "Desativada pelo ynoez" : status.State),
                    Tag = status
                });
            var action = new Button
            {
                Content = "Aplicar / restaurar selecionada",
                Style = (Style)FindResource("QuietButton"),
                IsEnabled = false
            };
            list.SelectionChanged += (_, _) => action.IsEnabled = list.SelectedItem is ListBoxItem;
            action.Click += async (_, _) =>
            {
                if (list.SelectedItem is not ListBoxItem item || item.Tag is not ScheduledTaskStatus selected)
                    return;
                var restore = selected.DisabledByYnoez;
                var prompt = restore
                    ? "Reativar a tarefa " + selected.Definition.DisplayName + "?"
                    : "Desativar a tarefa opcional " + selected.Definition.DisplayName + "? Ela deixará de executar automaticamente.";
                if (MessageBox.Show(prompt, "Confirmar tarefa", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;
                action.IsEnabled = false;
                try
                {
                    if (restore)
                        await Task.Run(() => manager.Restore(selected.Definition));
                    else
                        await Task.Run(() => manager.Disable(selected.Definition));
                    ShowPage("Debloat");
                }
                catch (Exception ex)
                {
                    MessageBox.Show("A operação na tarefa falhou: " + ex.Message, "ynoez", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };
            panel.Children.Add(list);
            panel.Children.Add(action);
            placeholder.Child = panel;
        }
        catch (Exception ex)
        {
            AppLog.Write("Consulta de tarefas opcionais no Debloat falhou: " + ex);
            placeholder.Child = new StackPanel
            {
                Margin = new Thickness(20),
                Children = { Heading("Tarefas opcionais", 14), Body("Consulta indisponível: " + ex.Message) }
            };
        }
    }

    private UIElement BuildMotherboard()
    {
        var root = PageContainer();
        var profile = MotherboardProfileService.Find(_hardware);
        var revisionHint = profile is null ? null : MotherboardProfileService.GetFirmwareRevisionHint(profile, _hardware.BoardVersion);
        var recognized = profile is not null;
        var title = recognized ? $"{profile!.Manufacturer} {profile!.Product}" : "Central da placa-mãe";
        var desc = recognized
            ? $"Perfil específico identificado. Indício SMBIOS: {(revisionHint?.Revision ?? "não informado")}. Esse valor não seleciona BIOS; confirme manualmente a revisão impressa no PCB."
            : "Modelo detectado: " + _hardware.BoardManufacturer + " " + _hardware.BoardProduct + ". Nenhum perfil de fabricante será forçado sem identificação confiável.";
        root.Children.Add(PageIntro(title, desc));

        var hardware = MakeCard();
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(Heading("Identificação e firmware", 15));
        panel.Children.Add(KeyValue("Fabricante", _hardware.BoardManufacturer));
        panel.Children.Add(KeyValue("Modelo", _hardware.BoardProduct));
        panel.Children.Add(KeyValue("Revisão SMBIOS", _hardware.BoardVersion));
        panel.Children.Add(KeyValue("BIOS", _hardware.BiosVersion));
        panel.Children.Add(KeyValue("Fabricante da BIOS", _hardware.BiosManufacturer));
        panel.Children.Add(KeyValue("Data da BIOS", _hardware.BiosReleaseDate));
        panel.Children.Add(KeyValue("Modelo do computador", _hardware.ComputerModel));
        panel.Children.Add(KeyValue("Adaptadores ativos", _hardware.NetworkAdapters));
        hardware.Child = panel;
        root.Children.Add(hardware);

        if (profile is not null)
        {
            root.Children.Add(ActionCard("Página oficial de suporte", "Abre a página genérica do modelo. Ela não pré-seleciona uma revisão e permite consultar documentação sem assumir qual PCB você possui.", "Abrir suporte genérico", () => OpenTargetAsync("https://www.gigabyte.com/br/Motherboard/A520M-K-V2"), "GIGABYTE"));
            if (!string.IsNullOrWhiteSpace(profile.ManualUrl))
                root.Children.Add(ActionCard("Manual da placa-mãe", "Documentação oficial da série A520M K. Confirme o sufixo V2 e conectores físicos antes de seguir instruções de firmware.", "Abrir manual", () => OpenTargetAsync(profile.ManualUrl), "DOCUMENTAÇÃO"));

            var revisionConfirmCard = MakeCard();
            var revisionConfirmPanel = new StackPanel { Margin = new Thickness(20) };
            revisionConfirmPanel.Children.Add(Heading("Confirmação manual da revisão do PCB", 15));
            revisionConfirmPanel.Children.Add(Body("Leia a revisão impressa diretamente na placa-mãe (serigrafia no PCB). Não use a versão SMBIOS, a versão da BIOS, a caixa ou uma página de compra como confirmação. Até você confirmar, nenhum link de BIOS específico por revisão será exibido."));
            revisionConfirmPanel.Children.Add(KeyValue("Indício reportado pelo SMBIOS (não é confirmação)", revisionHint?.Revision ?? "Não informado"));
            var revisionPicker = new ComboBox
            {
                Height = 38,
                MinWidth = 180,
                Padding = new Thickness(8, 5, 8, 5),
                Background = BrushFrom("#050505"),
                Foreground = PrimaryBrush,
                BorderBrush = BorderBrushSoft,
                BorderThickness = new Thickness(1),
                ItemsSource = profile.RevisionLinks,
                DisplayMemberPath = nameof(MotherboardRevisionLink.Revision),
                SelectedIndex = -1,
                ToolTip = "Escolha somente a revisão que você leu na serigrafia impressa no PCB"
            };
            var revisionConfirmStatus = Body("Nenhuma revisão física foi confirmada. Os links de BIOS específicos permanecem ocultos.");
            revisionConfirmStatus.TextWrapping = TextWrapping.Wrap;
            var confirmRevisionButton = new Button
            {
                Content = "Confirmar revisão física",
                Style = (Style)FindResource("AccentButton"),
                HorizontalAlignment = HorizontalAlignment.Left,
                IsEnabled = false,
                Margin = new Thickness(0, 8, 0, 10)
            };
            var revisionDetailsPanel = new StackPanel();
            revisionPicker.SelectionChanged += (_, _) =>
            {
                revisionDetailsPanel.Children.Clear();
                confirmRevisionButton.IsEnabled = revisionPicker.SelectedItem is MotherboardRevisionLink;
                revisionConfirmStatus.Text = revisionPicker.SelectedItem is MotherboardRevisionLink selected
                    ? "Selecionada " + selected.Revision + ", mas ainda não confirmada. Confira a mesma identificação impressa no PCB."
                    : "Nenhuma revisão física foi confirmada. Os links de BIOS específicos permanecem ocultos.";
            };
            confirmRevisionButton.Click += (_, _) =>
            {
                var selected = revisionPicker.SelectedItem as MotherboardRevisionLink;
                var confirmed = MotherboardProfileService.GetConfirmedPhysicalRevision(profile, selected?.Revision);
                if (confirmed is null)
                {
                    revisionConfirmStatus.Text = "A revisão escolhida não corresponde a uma opção cadastrada. Nenhum link de BIOS foi liberado.";
                    revisionDetailsPanel.Children.Clear();
                    return;
                }

                revisionConfirmStatus.Text = "Revisão " + confirmed.Revision +
                    " confirmada manualmente com base na serigrafia do PCB. Confira novamente o modelo completo antes de qualquer procedimento.";
                revisionDetailsPanel.Children.Clear();

                var biosCard = MakeCard();
                var biosPanel = new StackPanel { Margin = new Thickness(16) };
                biosPanel.Children.Add(Heading("BIOS específica — revisão " + confirmed.Revision, 14));
                biosPanel.Children.Add(KeyValue("Versão mais recente listada no perfil", confirmed.LatestBiosVersion));
                biosPanel.Children.Add(KeyValue("Data de publicação", confirmed.LatestBiosReleaseDate));
                foreach (var note in confirmed.LatestBiosNotes) biosPanel.Children.Add(Bullet(note));
                biosPanel.Children.Add(Body("Consulta verificada em " + confirmed.BiosCheckedAt +
                    ". Isto não significa que a atualização seja obrigatória. Compare a BIOS instalada, CPU suportada e notas oficiais; o ynoez não grava firmware."));
                biosCard.Child = biosPanel;
                revisionDetailsPanel.Children.Add(biosCard);
                revisionDetailsPanel.Children.Add(ActionCard(
                    "Página oficial de BIOS — revisão " + confirmed.Revision,
                    "Link liberado somente após confirmação manual da revisão física. A atualização continua manual.",
                    "Abrir página de BIOS",
                    () => OpenTargetAsync(confirmed.Bios),
                    "SOMENTE CONSULTA"));
                revisionDetailsPanel.Children.Add(ActionCard(
                    "Suporte oficial — revisão " + confirmed.Revision,
                    "Drivers e utilitários da revisão confirmada. Verifique versão do Windows e IDs dos dispositivos antes de instalar.",
                    "Abrir suporte da revisão",
                    () => OpenTargetAsync(confirmed.Support),
                    "SOMENTE CONSULTA"));
            };
            revisionConfirmPanel.Children.Add(revisionPicker);
            revisionConfirmPanel.Children.Add(confirmRevisionButton);
            revisionConfirmPanel.Children.Add(revisionConfirmStatus);
            revisionConfirmPanel.Children.Add(revisionDetailsPanel);
            revisionConfirmCard.Child = revisionConfirmPanel;
            root.Children.Add(revisionConfirmCard);

            var deviceCard = MakeCard();
            var devicePanel = new StackPanel { Margin = new Thickness(20) };
            devicePanel.Children.Add(Heading("Dispositivos reais de rede, áudio e sistema", 15));
            devicePanel.Children.Add(Body("Consulta o inventário PnP do Windows e associa os drivers assinados encontrados pelo Windows. Mostra ID de hardware, classe, fabricante, estado, provedor, versão, data e INF. Use os IDs para comparar com os pacotes oficiais da revisão física confirmada; o ynoez não instala nem atualiza drivers automaticamente."));
            var deviceStatus = Body("Inventário de dispositivos ainda não consultado.");
            var deviceDetails = Body("Selecione um dispositivo para ver seu ID de hardware e detalhes.");
            deviceDetails.TextWrapping = TextWrapping.Wrap;
            var deviceGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserResizeRows = false,
                SelectionMode = DataGridSelectionMode.Single,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                Background = BrushFrom("#050505"),
                Foreground = PrimaryBrush,
                RowBackground = BrushFrom("#050505"),
                AlternatingRowBackground = BrushFrom("#171717"),
                BorderBrush = BorderBrushSoft,
                Height = 300,
                Margin = new Thickness(0, 10, 0, 10)
            };
            deviceGrid.Columns.Add(new DataGridTextColumn { Header = "Classe", Binding = new Binding(nameof(MotherboardDeviceInfo.CategoryDisplay)), Width = new DataGridLength(100) });
            deviceGrid.Columns.Add(new DataGridTextColumn { Header = "Dispositivo", Binding = new Binding(nameof(MotherboardDeviceInfo.Name)), Width = new DataGridLength(1.8, DataGridLengthUnitType.Star) });
            deviceGrid.Columns.Add(new DataGridTextColumn { Header = "Fabricante", Binding = new Binding(nameof(MotherboardDeviceInfo.Manufacturer)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            deviceGrid.Columns.Add(new DataGridTextColumn { Header = "Estado", Binding = new Binding(nameof(MotherboardDeviceInfo.StateDisplay)), Width = new DataGridLength(185) });
            deviceGrid.Columns.Add(new DataGridTextColumn { Header = "Provedor do driver", Binding = new Binding(nameof(MotherboardDeviceInfo.DriverProviderDisplay)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            deviceGrid.Columns.Add(new DataGridTextColumn { Header = "Versão", Binding = new Binding(nameof(MotherboardDeviceInfo.DriverVersionDisplay)), Width = new DataGridLength(110) });
            deviceGrid.Columns.Add(new DataGridTextColumn { Header = "Data do driver", Binding = new Binding(nameof(MotherboardDeviceInfo.DriverDateDisplay)), Width = new DataGridLength(95) });
            IReadOnlyList<MotherboardDeviceInfo> motherboardDevices = Array.Empty<MotherboardDeviceInfo>();
            deviceGrid.SelectionChanged += (_, _) =>
            {
                if (deviceGrid.SelectedItem is not MotherboardDeviceInfo selected)
                {
                    deviceDetails.Text = "Selecione um dispositivo para ver seu ID de hardware e detalhes.";
                    return;
                }

                deviceDetails.Text = selected.Name +
                    "\nCategoria: " + selected.CategoryDisplay +
                    " • Fabricante: " + (string.IsNullOrWhiteSpace(selected.Manufacturer) ? "Não informado" : selected.Manufacturer) +
                    "\nEstado: " + selected.StateDisplay +
                    " • Serviço: " + (string.IsNullOrWhiteSpace(selected.ServiceName) ? "Não informado" : selected.ServiceName) +
                    "\nDriver: " + selected.DriverProviderDisplay + " • " + selected.DriverVersionDisplay +
                    " • " + selected.DriverDateDisplay + " • INF: " +
                    (string.IsNullOrWhiteSpace(selected.InfName) ? "Não informado" : selected.InfName) +
                    "\nDeviceID: " + selected.DeviceId +
                    "\nHardware IDs:\n" + selected.HardwareIdsDisplay;
            };
            var deviceButtons = new WrapPanel();
            var queryDevices = new Button { Content = "Consultar dispositivos", Style = (Style)FindResource("AccentButton"), Margin = new Thickness(0, 0, 8, 0) };
            var cancelDevices = new Button { Content = "Cancelar", Style = (Style)FindResource("QuietButton"), IsEnabled = false, Margin = new Thickness(0, 0, 8, 0) };
            var exportDevices = new Button { Content = "Exportar CSV", Style = (Style)FindResource("QuietButton"), IsEnabled = false };
            queryDevices.Click += async (_, _) =>
            {
                if (_motherboardDeviceInventoryCts is not null) return;
                var cts = new CancellationTokenSource();
                _motherboardDeviceInventoryCts = cts;
                queryDevices.IsEnabled = false;
                cancelDevices.IsEnabled = true;
                exportDevices.IsEnabled = false;
                deviceStatus.Text = "Consultando dispositivos PnP e drivers assinados…";
                try
                {
                    var snapshot = await MotherboardDeviceInventoryService.CaptureAsync(cts.Token);
                    if (!Dispatcher.HasShutdownStarted)
                    {
                        motherboardDevices = snapshot.Devices;
                        deviceGrid.ItemsSource = motherboardDevices;
                        deviceStatus.Text = snapshot.DeviceCount + " dispositivos • " +
                            snapshot.DevicesWithReportedErrors + " com código de configuração diferente de zero • " +
                            snapshot.CapturedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") +
                            "\nA data/provedor são o que Win32_PnPSignedDriver reporta; ausência de dados não prova que o driver seja inválido.";
                        exportDevices.IsEnabled = motherboardDevices.Count > 0;
                    }
                }
                catch (OperationCanceledException)
                {
                    if (!Dispatcher.HasShutdownStarted) deviceStatus.Text = "Consulta cancelada. Nenhum driver foi alterado.";
                }
                catch (Exception ex)
                {
                    AppLog.Write("Inventário de dispositivos da placa-mãe falhou: " + ex);
                    if (!Dispatcher.HasShutdownStarted) deviceStatus.Text = "Não foi possível consultar dispositivos: " + ex.Message;
                }
                finally
                {
                    if (ReferenceEquals(_motherboardDeviceInventoryCts, cts)) _motherboardDeviceInventoryCts = null;
                    cts.Dispose();
                    if (!Dispatcher.HasShutdownStarted)
                    {
                        queryDevices.IsEnabled = true;
                        cancelDevices.IsEnabled = false;
                    }
                }
            };
            cancelDevices.Click += (_, _) => _motherboardDeviceInventoryCts?.Cancel();
            exportDevices.Click += (_, _) =>
            {
                if (motherboardDevices.Count == 0) return;
                var dialog = new SaveFileDialog
                {
                    Title = "Exportar inventário de dispositivos da placa-mãe",
                    Filter = "CSV (*.csv)|*.csv",
                    FileName = "ynoez-dispositivos-placa-mae.csv",
                    AddExtension = true,
                    DefaultExt = ".csv"
                };
                if (dialog.ShowDialog(this) != true) return;
                try
                {
                    var snapshot = new MotherboardDeviceInventorySnapshot(DateTimeOffset.Now, motherboardDevices);
                    File.WriteAllText(dialog.FileName, MotherboardDeviceInventoryService.BuildCsv(snapshot), new System.Text.UTF8Encoding(true));
                    deviceStatus.Text = "Inventário de dispositivos exportado: " + dialog.FileName;
                }
                catch (Exception ex)
                {
                    AppLog.Write("Falha ao exportar inventário de dispositivos: " + ex);
                    MessageBox.Show("Não foi possível exportar o CSV: " + ex.Message, "ynoez", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };
            deviceButtons.Children.Add(queryDevices);
            deviceButtons.Children.Add(cancelDevices);
            deviceButtons.Children.Add(exportDevices);
            devicePanel.Children.Add(deviceStatus);
            devicePanel.Children.Add(deviceGrid);
            devicePanel.Children.Add(deviceDetails);
            devicePanel.Children.Add(deviceButtons);
            deviceCard.Child = devicePanel;
            root.Children.Add(deviceCard);

            var specs = MakeCard();
            var specsPanel = new StackPanel { Margin = new Thickness(20) };
            specsPanel.Children.Add(Heading("Plataforma reconhecida", 15));
            specsPanel.Children.Add(KeyValue("Chipset", profile.Chipset));
            specsPanel.Children.Add(KeyValue("Soquete", profile.Platform));
            specsPanel.Children.Add(KeyValue("Memória", profile.Memory));
            specsPanel.Children.Add(KeyValue("Rede", profile.Network));
            specsPanel.Children.Add(KeyValue("Áudio integrado", profile.Audio));
            specsPanel.Children.Add(Body("Recursos: " + string.Join(" • ", profile.Features)));
            specsPanel.Children.Add(Body("Dados do perfil: " + Path.Combine(AppPaths.Toolbox, "Placas-mae", "gigabyte-a520m-k-v2.json")));
            specs.Child = specsPanel;
            root.Children.Add(specs);

            var utilities = MakeCard();
            var utilityPanel = new StackPanel { Margin = new Thickness(20) };
            utilityPanel.Children.Add(Heading("Central de utilitários Gigabyte", 15));
            foreach (var utility in profile.Utilities)
            {
                utilityPanel.Children.Add(Heading(utility.Name, 11));
                utilityPanel.Children.Add(Body(utility.Purpose));
                utilityPanel.Children.Add(Body("Cuidado: " + utility.Safety));
                utilityPanel.Children.Add(new Border { Height = 1, Background = BorderBrushSoft, Margin = new Thickness(0, 9, 0, 9) });
            }
            utilities.Child = utilityPanel;
            root.Children.Add(utilities);

            var recommendations = MakeCard();
            var recPanel = new StackPanel { Margin = new Thickness(20) };
            recPanel.Children.Add(Heading("Configurações e otimizações seguras", 15));
            foreach (var recommendation in profile.SafeRecommendations)
                recPanel.Children.Add(Bullet(recommendation));
            recPanel.Children.Add(Heading("Bloqueado para automação", 13));
            foreach (var blocked in profile.BlockedAutomaticActions)
                recPanel.Children.Add(Bullet(blocked, "!"));
            recommendations.Child = recPanel;
            root.Children.Add(recommendations);
        }
        else
        {
            root.Children.Add(NoteCard("Perfil ainda não cadastrado", "O ynoez detectou o hardware, mas ainda não tem um perfil verificado para esse modelo. O cadastro precisa conter revisão, páginas oficiais, utilitários suportados e limites de segurança antes de habilitar recomendações específicas."));
        }
        return root;
    }

    private UIElement BuildToolbox()
    {
        var root = PageContainer();
        root.Children.Add(PageIntro("Toolbox", "Pesquise configurações, serviços e ferramentas do Windows. Abra o destino oficial sem alterar nada."));
        var search = new TextBox
        {
            Height = 42, FontSize = 13, Padding = new Thickness(13, 10, 13, 10),
            Background = CardBrush, Foreground = PrimaryBrush, BorderBrush = BorderBrushSoft,
            BorderThickness = new Thickness(1), CaretBrush = AccentBrush,
            ToolTip = "Pesquisar Windows Update, Search, impressora, Defender…"
        };
        var results = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        void RenderResults()
        {
            results.Children.Clear();
            var query = search.Text.Trim();
            var hits = string.IsNullOrWhiteSpace(query)
                ? ToolboxCatalog.Take(6).ToArray()
                : ToolboxCatalog.Where(x => x.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || x.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || x.Keywords.Any(k => k.Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (hits.Length == 0)
            {
                results.Children.Add(NoteCard("Nenhum resultado", "Não encontrei uma entrada correspondente. Experimente pesquisar pelo nome do recurso ou pelo serviço do Windows."));
                return;
            }
            foreach (var hit in hits)
            {
                var target = hit.Target;
                var description = hit.Description;
                if (!string.IsNullOrWhiteSpace(hit.ServiceName))
                    description += "\n" + ReadServiceState(hit.ServiceName);
                results.Children.Add(ActionCard(hit.Name, description, "Abrir", () => OpenTargetAsync(target), hit.Kind.ToUpperInvariant()));
            }
        }
        search.TextChanged += (_, _) => RenderResults();
        root.Children.Add(search);
        root.Children.Add(results);

        var serviceCard = MakeCard();
        var servicePanel = new StackPanel { Margin = new Thickness(20) };
        servicePanel.Children.Add(Heading("Inventário ao vivo de serviços", 15));
        servicePanel.Children.Add(Body("Consulta todos os serviços registrados no Windows, incluindo nome interno, descrição, estado, tipo de inicialização e PID. A classificação é apenas informativa: protegidos, opcionais que já constam na whitelist de Debloat e serviços sem política de alteração. Esta tela não altera nem encerra serviços."));
        var serviceSearch = new TextBox
        {
            Height = 38,
            FontSize = 12,
            Padding = new Thickness(11, 8, 11, 8),
            Background = BrushFrom("#050505"),
            Foreground = PrimaryBrush,
            BorderBrush = BorderBrushSoft,
            BorderThickness = new Thickness(1),
            CaretBrush = AccentBrush,
            ToolTip = "Pesquisar pelo nome, serviço, descrição, estado ou classificação"
        };
        var serviceStatus = Body("A consulta ainda não foi executada.");
        var serviceDetails = Body("Selecione um serviço para ver a explicação da classificação.");
        serviceDetails.TextWrapping = TextWrapping.Wrap;
        var serviceGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserResizeRows = false,
            SelectionMode = DataGridSelectionMode.Single,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            Background = BrushFrom("#050505"),
            Foreground = PrimaryBrush,
            RowBackground = BrushFrom("#050505"),
            AlternatingRowBackground = BrushFrom("#171717"),
            BorderBrush = BorderBrushSoft,
            Height = 330,
            Margin = new Thickness(0, 10, 0, 10)
        };
        serviceGrid.Columns.Add(new DataGridTextColumn { Header = "Serviço", Binding = new Binding(nameof(WindowsServiceInfo.Name)), Width = new DataGridLength(1.0, DataGridLengthUnitType.Star) });
        serviceGrid.Columns.Add(new DataGridTextColumn { Header = "Nome de exibição", Binding = new Binding(nameof(WindowsServiceInfo.DisplayName)), Width = new DataGridLength(1.5, DataGridLengthUnitType.Star) });
        serviceGrid.Columns.Add(new DataGridTextColumn { Header = "Estado", Binding = new Binding(nameof(WindowsServiceInfo.StateDisplay)), Width = new DataGridLength(100) });
        serviceGrid.Columns.Add(new DataGridTextColumn { Header = "Inicialização", Binding = new Binding(nameof(WindowsServiceInfo.StartModeDisplay)), Width = new DataGridLength(105) });
        serviceGrid.Columns.Add(new DataGridTextColumn { Header = "PID", Binding = new Binding(nameof(WindowsServiceInfo.ProcessIdDisplay)), Width = new DataGridLength(55) });
        serviceGrid.Columns.Add(new DataGridTextColumn { Header = "Política", Binding = new Binding(nameof(WindowsServiceInfo.SafetyDisplay)), Width = new DataGridLength(150) });
        serviceGrid.Columns.Add(new DataGridTextColumn { Header = "Descrição", Binding = new Binding(nameof(WindowsServiceInfo.Description)), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        IReadOnlyList<WindowsServiceInfo> allServices = Array.Empty<WindowsServiceInfo>();
        void RenderServices()
        {
            var query = serviceSearch.Text.Trim();
            var filtered = string.IsNullOrWhiteSpace(query)
                ? allServices
                : allServices.Where(x =>
                    x.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    x.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    x.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    x.StateDisplay.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    x.StartModeDisplay.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    x.Safety.Category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    x.Safety.Explanation.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
            serviceGrid.ItemsSource = filtered;
            serviceStatus.Text = allServices.Count == 0
                ? "Nenhum inventário carregado."
                : filtered.Count + " de " + allServices.Count + " serviços • somente leitura";
        }
        serviceSearch.TextChanged += (_, _) => RenderServices();
        serviceGrid.SelectionChanged += (_, _) =>
        {
            if (serviceGrid.SelectedItem is not WindowsServiceInfo selected)
            {
                serviceDetails.Text = "Selecione um serviço para ver a explicação da classificação.";
                return;
            }
            serviceDetails.Text = selected.DisplayName + " (" + selected.Name + ")" +
                "\nEstado: " + selected.StateDisplay +
                " • Inicialização: " + selected.StartModeDisplay +
                " • PID: " + selected.ProcessIdDisplay +
                "\nPolítica: " + selected.Safety.Explanation +
                (string.IsNullOrWhiteSpace(selected.Description) ? "" : "\nDescrição: " + selected.Description);
        };
        var serviceButtons = new WrapPanel();
        var refreshServices = new Button { Content = "Consultar serviços", Style = (Style)FindResource("AccentButton"), Margin = new Thickness(0, 0, 8, 0) };
        var cancelServices = new Button { Content = "Cancelar", Style = (Style)FindResource("QuietButton"), IsEnabled = false, Margin = new Thickness(0, 0, 8, 0) };
        var exportServices = new Button { Content = "Exportar CSV", Style = (Style)FindResource("QuietButton"), IsEnabled = false, Margin = new Thickness(0, 0, 8, 0) };
        var openServicesConsole = new Button { Content = "Abrir services.msc", Style = (Style)FindResource("QuietButton") };
        refreshServices.Click += async (_, _) =>
        {
            if (_serviceInventoryCts is not null) return;
            var cts = new CancellationTokenSource();
            _serviceInventoryCts = cts;
            refreshServices.IsEnabled = false;
            cancelServices.IsEnabled = true;
            exportServices.IsEnabled = false;
            serviceStatus.Text = "Consultando Win32_Service. A operação pode levar alguns segundos…";
            try
            {
                var captured = await WindowsServiceInventoryService.CaptureAsync(cts.Token);
                if (!Dispatcher.HasShutdownStarted)
                {
                    allServices = captured;
                    RenderServices();
                    exportServices.IsEnabled = allServices.Count > 0;
                    serviceStatus.Text = allServices.Count + " serviços inventariados • nenhum serviço foi alterado";
                }
            }
            catch (OperationCanceledException)
            {
                if (!Dispatcher.HasShutdownStarted) serviceStatus.Text = "Consulta cancelada. Nenhuma alteração foi feita.";
            }
            catch (Exception ex)
            {
                AppLog.Write("Inventário de serviços do Windows falhou: " + ex);
                if (!Dispatcher.HasShutdownStarted) serviceStatus.Text = "Falha na consulta: " + ex.Message;
            }
            finally
            {
                if (ReferenceEquals(_serviceInventoryCts, cts)) _serviceInventoryCts = null;
                cts.Dispose();
                if (!Dispatcher.HasShutdownStarted)
                {
                    refreshServices.IsEnabled = true;
                    cancelServices.IsEnabled = false;
                }
            }
        };
        cancelServices.Click += (_, _) => _serviceInventoryCts?.Cancel();
        exportServices.Click += (_, _) =>
        {
            if (allServices.Count == 0) return;
            var dialog = new SaveFileDialog
            {
                Title = "Exportar inventário de serviços",
                Filter = "CSV (*.csv)|*.csv",
                FileName = "ynoez-servicos-windows.csv",
                AddExtension = true,
                DefaultExt = ".csv"
            };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                File.WriteAllText(dialog.FileName, WindowsServiceInventoryService.BuildCsv(allServices), new System.Text.UTF8Encoding(true));
                serviceStatus.Text = "Inventário completo exportado: " + dialog.FileName;
            }
            catch (Exception ex)
            {
                AppLog.Write("Falha ao exportar inventário de serviços: " + ex);
                MessageBox.Show("Não foi possível exportar o CSV: " + ex.Message, "ynoez", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        openServicesConsole.Click += async (_, _) => await OpenTargetAsync("services.msc");
        serviceButtons.Children.Add(refreshServices);
        serviceButtons.Children.Add(cancelServices);
        serviceButtons.Children.Add(exportServices);
        serviceButtons.Children.Add(openServicesConsole);
        servicePanel.Children.Add(serviceSearch);
        servicePanel.Children.Add(serviceStatus);
        servicePanel.Children.Add(serviceGrid);
        servicePanel.Children.Add(serviceDetails);
        servicePanel.Children.Add(serviceButtons);
        serviceCard.Child = servicePanel;
        root.Children.Add(serviceCard);

        var taskCard = MakeCard();
        var taskPanel = new StackPanel { Margin = new Thickness(20) };
        taskPanel.Children.Add(Heading("Inventário ao vivo de tarefas agendadas", 15));
        taskPanel.Children.Add(Body("Consulta as tarefas registradas no Agendador do Windows: nome, caminho, estado, autor, descrição, última execução, próxima execução e último resultado. A classificação separa tarefas protegidas, seis tarefas opcionais avaliadas pelo Debloat e todo o restante como somente leitura. Esta tela não habilita, desabilita, inicia ou encerra tarefas."));
        var taskSearch = new TextBox
        {
            Height = 38,
            FontSize = 12,
            Padding = new Thickness(11, 8, 11, 8),
            Background = BrushFrom("#050505"),
            Foreground = PrimaryBrush,
            BorderBrush = BorderBrushSoft,
            BorderThickness = new Thickness(1),
            CaretBrush = AccentBrush,
            ToolTip = "Pesquisar nome, caminho, estado, autor, descrição ou classificação"
        };
        var taskStatus = Body("A consulta ainda não foi executada.");
        var taskDetails = Body("Selecione uma tarefa para ver sua classificação e descrição.");
        taskDetails.TextWrapping = TextWrapping.Wrap;
        var taskGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserResizeRows = false,
            SelectionMode = DataGridSelectionMode.Single,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            Background = BrushFrom("#050505"),
            Foreground = PrimaryBrush,
            RowBackground = BrushFrom("#050505"),
            AlternatingRowBackground = BrushFrom("#171717"),
            BorderBrush = BorderBrushSoft,
            Height = 330,
            Margin = new Thickness(0, 10, 0, 10)
        };
        taskGrid.Columns.Add(new DataGridTextColumn { Header = "Tarefa", Binding = new Binding(nameof(WindowsScheduledTaskInfo.TaskName)), Width = new DataGridLength(1.2, DataGridLengthUnitType.Star) });
        taskGrid.Columns.Add(new DataGridTextColumn { Header = "Caminho", Binding = new Binding(nameof(WindowsScheduledTaskInfo.TaskPath)), Width = new DataGridLength(1.8, DataGridLengthUnitType.Star) });
        taskGrid.Columns.Add(new DataGridTextColumn { Header = "Estado", Binding = new Binding(nameof(WindowsScheduledTaskInfo.StateDisplay)), Width = new DataGridLength(105) });
        taskGrid.Columns.Add(new DataGridTextColumn { Header = "Política", Binding = new Binding(nameof(WindowsScheduledTaskInfo.SafetyDisplay)), Width = new DataGridLength(150) });
        taskGrid.Columns.Add(new DataGridTextColumn { Header = "Última execução", Binding = new Binding(nameof(WindowsScheduledTaskInfo.LastRunDisplay)), Width = new DataGridLength(135) });
        taskGrid.Columns.Add(new DataGridTextColumn { Header = "Próxima execução", Binding = new Binding(nameof(WindowsScheduledTaskInfo.NextRunDisplay)), Width = new DataGridLength(135) });
        IReadOnlyList<WindowsScheduledTaskInfo> allTasks = Array.Empty<WindowsScheduledTaskInfo>();
        void RenderTasks()
        {
            var query = taskSearch.Text.Trim();
            var filtered = string.IsNullOrWhiteSpace(query)
                ? allTasks
                : allTasks.Where(x =>
                    x.TaskName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    x.TaskPath.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    x.StateDisplay.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    x.Author.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    x.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    x.Safety.Category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    x.Safety.Explanation.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
            taskGrid.ItemsSource = filtered;
            taskStatus.Text = allTasks.Count == 0
                ? "Nenhum inventário carregado."
                : filtered.Count + " de " + allTasks.Count + " tarefas • " +
                  allTasks.Count(x => x.Safety.IsProtected) + " protegidas • " +
                  allTasks.Count(x => x.Safety.IsOptional) + " opcionais avaliadas • somente leitura";
        }
        taskSearch.TextChanged += (_, _) => RenderTasks();
        taskGrid.SelectionChanged += (_, _) =>
        {
            if (taskGrid.SelectedItem is not WindowsScheduledTaskInfo selected)
            {
                taskDetails.Text = "Selecione uma tarefa para ver sua classificação e descrição.";
                return;
            }
            taskDetails.Text = selected.FullName +
                "\nEstado: " + selected.StateDisplay +
                " • Autor: " + (string.IsNullOrWhiteSpace(selected.Author) ? "Não informado" : selected.Author) +
                " • Último resultado: " + selected.LastResultDisplay +
                "\nÚltima execução: " + selected.LastRunDisplay +
                " • Próxima execução: " + selected.NextRunDisplay +
                "\nPolítica: " + selected.Safety.Explanation +
                (string.IsNullOrWhiteSpace(selected.Description) ? "" : "\nDescrição: " + selected.Description);
        };
        var taskButtons = new WrapPanel();
        var refreshTasks = new Button { Content = "Consultar tarefas", Style = (Style)FindResource("AccentButton"), Margin = new Thickness(0, 0, 8, 0) };
        var cancelTasks = new Button { Content = "Cancelar", Style = (Style)FindResource("QuietButton"), IsEnabled = false, Margin = new Thickness(0, 0, 8, 0) };
        var exportTasks = new Button { Content = "Exportar CSV", Style = (Style)FindResource("QuietButton"), IsEnabled = false, Margin = new Thickness(0, 0, 8, 0) };
        var openTaskScheduler = new Button { Content = "Abrir taskschd.msc", Style = (Style)FindResource("QuietButton") };
        refreshTasks.Click += async (_, _) =>
        {
            if (_scheduledTaskInventoryCts is not null) return;
            var cts = new CancellationTokenSource();
            _scheduledTaskInventoryCts = cts;
            refreshTasks.IsEnabled = false;
            cancelTasks.IsEnabled = true;
            exportTasks.IsEnabled = false;
            taskStatus.Text = "Consultando o Agendador de Tarefas. A primeira leitura pode levar alguns segundos…";
            try
            {
                var captured = await WindowsScheduledTaskInventoryService.CaptureAsync(cts.Token);
                if (!Dispatcher.HasShutdownStarted)
                {
                    allTasks = captured;
                    RenderTasks();
                    exportTasks.IsEnabled = allTasks.Count > 0;
                    taskStatus.Text = allTasks.Count + " tarefas inventariadas • consulta somente leitura; nenhuma tarefa foi alterada.";
                }
            }
            catch (OperationCanceledException)
            {
                if (!Dispatcher.HasShutdownStarted) taskStatus.Text = "Consulta cancelada. Nenhuma tarefa foi alterada.";
            }
            catch (Exception ex)
            {
                AppLog.Write("Inventário de tarefas agendadas falhou: " + ex);
                if (!Dispatcher.HasShutdownStarted) taskStatus.Text = "Falha na consulta: " + ex.Message;
            }
            finally
            {
                if (ReferenceEquals(_scheduledTaskInventoryCts, cts)) _scheduledTaskInventoryCts = null;
                cts.Dispose();
                if (!Dispatcher.HasShutdownStarted)
                {
                    refreshTasks.IsEnabled = true;
                    cancelTasks.IsEnabled = false;
                }
            }
        };
        cancelTasks.Click += (_, _) => _scheduledTaskInventoryCts?.Cancel();
        exportTasks.Click += (_, _) =>
        {
            if (allTasks.Count == 0) return;
            var dialog = new SaveFileDialog
            {
                Title = "Exportar inventário de tarefas agendadas",
                Filter = "CSV (*.csv)|*.csv",
                FileName = "ynoez-tarefas-agendadas.csv",
                AddExtension = true,
                DefaultExt = ".csv"
            };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                File.WriteAllText(dialog.FileName, WindowsScheduledTaskInventoryService.BuildCsv(allTasks), new System.Text.UTF8Encoding(true));
                taskStatus.Text = "Inventário de tarefas exportado: " + dialog.FileName;
            }
            catch (Exception ex)
            {
                AppLog.Write("Falha ao exportar inventário de tarefas: " + ex);
                MessageBox.Show("Não foi possível exportar o CSV: " + ex.Message, "ynoez", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        openTaskScheduler.Click += async (_, _) => await OpenTargetAsync("taskschd.msc");
        taskButtons.Children.Add(refreshTasks);
        taskButtons.Children.Add(cancelTasks);
        taskButtons.Children.Add(exportTasks);
        taskButtons.Children.Add(openTaskScheduler);
        taskPanel.Children.Add(taskSearch);
        taskPanel.Children.Add(taskStatus);
        taskPanel.Children.Add(taskGrid);
        taskPanel.Children.Add(taskDetails);
        taskPanel.Children.Add(taskButtons);
        taskCard.Child = taskPanel;
        root.Children.Add(taskCard);

        root.Children.Add(NoteCard("Ações de serviço e tarefas", "A Toolbox é somente leitura. A página Debloat contém listas curtas de seis serviços e seis tarefas opcionais, com backup, confirmação, verificação e restauração; não há desligamento em massa. Windows Search, impressão, Store, Defender, Firewall, Windows Update, manutenção, rede, áudio e anti-cheats permanecem protegidos."));
        RenderResults();
        return root;
    }

    private UIElement BuildRestore()
    {
        var root = PageContainer();
        root.Children.Add(PageIntro("Restore", "Central de recuperação do ynoez. Cada backup permanece separado e só é consumido depois da verificação da restauração."));

        root.Children.Add(Heading("Diagnóstico e reparo do Windows", 15));
        root.Children.Add(Body("O DISM verifica/repara o repositório de componentes e o SFC verifica/repara arquivos protegidos do Windows. As verificações são somente leitura; os dois reparos exigem confirmação e UAC. As operações podem levar bastante tempo. O ynoez salva o resultado em Logs e nunca reinicia o computador automaticamente."));
        var repairCard = MakeCard();
        var repairPanel = new StackPanel { Margin = new Thickness(20) };
        var repairStatus = Body("Nenhuma verificação executada.");
        var repairButtons = new WrapPanel();
        var repairCancel = new Button { Content = "Cancelar", Style = (Style)FindResource("QuietButton"), IsEnabled = false };
        var repairActions = WindowsRepairService.GetActions();
        var actionButtons = new List<Button>();
        foreach (var actionInfo in repairActions)
        {
            var button = new Button
            {
                Content = actionInfo.Name,
                Style = actionInfo.IsRepair ? (Style)FindResource("AccentButton") : (Style)FindResource("QuietButton"),
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(10, 7, 10, 7),
                ToolTip = actionInfo.Description
            };
            button.Click += async (_, _) =>
            {
                if (_windowsRepairCts is not null) return;
                if (actionInfo.IsRepair && MessageBox.Show(
                    actionInfo.Description + "\n\nO Windows poderá usar o Windows Update e pode precisar reiniciar depois. O ynoez não fará reinicialização automática. Continuar?",
                    "Confirmar reparo do Windows", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;

                _windowsRepairCts = new CancellationTokenSource();
                var token = _windowsRepairCts.Token;
                foreach (var item in actionButtons) item.IsEnabled = false;
                repairCancel.IsEnabled = true;
                repairStatus.Text = actionInfo.Name + " — solicitando elevação UAC…";
                var progress = new Progress<string>(message => repairStatus.Text = message);
                try
                {
                    var result = await Task.Run(
                        () => WindowsRepairService.RunAsync(actionInfo.Action, progress, token), token);
                    repairStatus.Text = (result.Succeeded ? "Concluído" : "Finalizado com erro") +
                        " • código " + result.ExitCode + "\nInício: " + result.StartedAt.ToLocalTime().ToString("HH:mm:ss") +
                        " • Fim: " + result.FinishedAt.ToLocalTime().ToString("HH:mm:ss") +
                        "\nLog: " + result.LogPath + "\n\n" + result.OutputTail;
                    MessageBox.Show(
                        (result.Succeeded ? "Operação concluída." : "O comando terminou com erro.") +
                        "\nCódigo: " + result.ExitCode + "\nLog: " + result.LogPath,
                        "ynoez — manutenção do Windows",
                        MessageBoxButton.OK,
                        result.Succeeded ? MessageBoxImage.Information : MessageBoxImage.Warning);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    repairStatus.Text = "Operação cancelada. O log parcial, quando existente, foi preservado.";
                }
                catch (Exception ex)
                {
                    AppLog.Write("Manutenção do Windows falhou: " + ex);
                    repairStatus.Text = "Falha: " + ex.Message;
                }
                finally
                {
                    _windowsRepairCts?.Dispose();
                    _windowsRepairCts = null;
                    foreach (var item in actionButtons) item.IsEnabled = true;
                    repairCancel.IsEnabled = false;
                }
            };
            actionButtons.Add(button);
            repairButtons.Children.Add(button);
        }
        repairCancel.Click += (_, _) => _windowsRepairCts?.Cancel();
        repairPanel.Children.Add(repairButtons);
        repairPanel.Children.Add(repairCancel);
        repairPanel.Children.Add(repairStatus);
        repairPanel.Children.Add(NoteCard("Antes do reparo", "SFC e DISM são ferramentas oficiais do Windows, mas um reparo pode consumir dados e tempo. Salve o trabalho, mantenha o computador ligado e não feche a janela de manutenção durante a execução. Para falhas de hardware, o reparo de arquivos não substitui diagnóstico físico."));
        repairCard.Child = repairPanel;
        root.Children.Add(repairCard);

        root.Children.Add(ActionCard("Restauração do Sistema", "Abre o utilitário nativo do Windows para escolher um ponto de restauração disponível.", "Abrir Restauração", () => OpenTargetAsync("rstrui.exe"), "WINDOWS"));
        root.Children.Add(Heading("Planos de energia", 15));
        if (PowerProfileService.CanRestoreOriginal())
        {
            root.Children.Add(ActionCard("Restaurar plano base do ynoez",
                "Recupera o plano exportado antes do primeiro perfil manual, com verificação e reimportação do arquivo .pow quando necessário.",
                "Restaurar plano original", () =>
                {
                    if (_autoProfileCts is not null)
                    {
                        MessageBox.Show("Pare os perfis automáticos antes de restaurar o plano original.", "ynoez", MessageBoxButton.OK, MessageBoxImage.Information);
                        return Task.CompletedTask;
                    }
                    var message = PowerProfileService.RestoreOriginal();
                    MessageBox.Show(message, "ynoez", MessageBoxButton.OK, MessageBoxImage.Information);
                    ShowPage("Restore");
                    return Task.CompletedTask;
                }, "BACKUP"));
        }
        else
            root.Children.Add(NoteCard("Plano base", "Nenhum backup persistente de plano de energia está aguardando restauração."));

        if (PowerProfileService.HasInterruptedAutomaticSession)
        {
            root.Children.Add(ActionCard("Recuperar sessão automática interrompida",
                "O ynoez encontrou metadados de uma sessão automática pendente. A recuperação tenta reativar o plano original e só remove o backup depois de verificar o resultado.",
                "Recuperar sessão", () =>
                {
                    try
                    {
                        PowerProfileService.RecoverInterruptedAutomaticSession();
                        MessageBox.Show("Sessão automática recuperada e plano anterior verificado.", "ynoez",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Write("Recuperação manual da sessão automática falhou: " + ex);
                        MessageBox.Show(ex.Message + "\n\nO backup foi preservado.", "ynoez",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    ShowPage("Restore");
                    return Task.CompletedTask;
                }, "RECUPERAÇÃO"));
        }

        if (BackupService.HasPreviousPowerSchemeBackup)
        {
            root.Children.Add(ActionCard("Restaurar plano de energia legado",
                "Recupera um backup de plano criado por versões antigas do ynoez. O arquivo só será removido após confirmação do plano ativo pelo Windows.",
                "Restaurar backup antigo", () =>
                {
                    if (_autoProfileCts is not null)
                    {
                        MessageBox.Show("Pare os perfis automáticos antes de restaurar energia.", "ynoez", MessageBoxButton.OK, MessageBoxImage.Information);
                        return Task.CompletedTask;
                    }
                    var message = BackupService.RestorePreviousPowerScheme();
                    MessageBox.Show(message, "ynoez", MessageBoxButton.OK, MessageBoxImage.Information);
                    ShowPage("Restore");
                    return Task.CompletedTask;
                }, "LEGADO"));
        }

        root.Children.Add(Heading("Backups de Registro", 15));
        try
        {
            var tweaks = OptimizationCatalogService.LoadRegistryTweaks();
            var registry = new RegistryTweakService();
            var pending = tweaks.Where(x => registry.CanRestore(x.Id)).ToArray();
            if (pending.Length == 0)
                root.Children.Add(NoteCard("Registro", "Nenhum ajuste de Registro está aguardando restauração."));
            foreach (var tweak in pending)
            {
                root.Children.Add(ActionCard(tweak.Name,
                    tweak.Description + "\nCategoria: " + tweak.Category + " | Risco: " + tweak.Risk +
                    "\nO backup original foi criado antes da primeira alteração.",
                    "Restaurar valor original", () =>
                    {
                        if (MessageBox.Show("Restaurar o Registro para o estado anterior de \"" + tweak.Name + "\"?",
                            "Confirmar restauração", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                            return Task.CompletedTask;
                        registry.Restore(tweak);
                        MessageBox.Show("Valor original restaurado e verificado. O backup foi consumido.", "ynoez",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        ShowPage("Restore");
                        return Task.CompletedTask;
                    }, "BACKUP"));
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("Central de restauração do Registro falhou: " + ex);
            root.Children.Add(NoteCard("Backups de Registro precisam de atenção", ex.Message));
        }

        root.Children.Add(Heading("Serviços opcionais", 15));
        try
        {
            var manager = new ServiceTweakService();
            var pending = manager.GetStatuses().Where(x => x.DisabledByYnoez).ToArray();
            if (pending.Length == 0)
                root.Children.Add(NoteCard("Serviços", "Nenhum serviço opcional desativado pelo ynoez está aguardando restauração."));
            foreach (var service in pending)
            {
                if (!service.Available)
                {
                    root.Children.Add(ActionCard(service.Definition.DisplayName + " — tentativa de recuperação",
                        service.Definition.Description + "\n" + service.State +
                        "\nA consulta WMI falhou ou o serviço não foi encontrado. Você pode tentar a restauração explícita via PowerShell; ela só consome o backup se o estado anterior for confirmado.",
                        "Tentar restaurar", async () =>
                        {
                            if (MessageBox.Show("Tentar restaurar o serviço \"" + service.Definition.Name +
                                "\" a partir do backup, mesmo sem consulta WMI disponível? O backup será mantido se a confirmação falhar.",
                                "Confirmar recuperação", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                                return;
                            await Task.Run(() => manager.Restore(service.Definition));
                            MessageBox.Show("Restauração confirmada pelo Windows.", "ynoez", MessageBoxButton.OK, MessageBoxImage.Information);
                            ShowPage("Restore");
                        }, "BACKUP"));
                    continue;
                }
                root.Children.Add(ActionCard(service.Definition.DisplayName,
                    service.Definition.Description + "\nEstado atual: " + service.State +
                    "\nInicialização atual: " + service.StartMode,
                    "Restaurar serviço", async () =>
                    {
                        if (MessageBox.Show("Restaurar o serviço \"" + service.Definition.Name + "\" ao estado salvo?",
                            "Confirmar restauração", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                            return;
                        await Task.Run(() => manager.Restore(service.Definition));
                        MessageBox.Show("Serviço restaurado e verificado.", "ynoez", MessageBoxButton.OK, MessageBoxImage.Information);
                        ShowPage("Restore");
                    }, "BACKUP"));
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("Central de restauração dos serviços falhou: " + ex);
            root.Children.Add(NoteCard("Backups de serviços precisam de atenção", ex.Message));
        }

        root.Children.Add(Heading("Energia dos adaptadores de rede", 15));
        try
        {
            var manager = new AdapterPowerManagementService();
            var pending = manager.GetPendingRestores();
            if (pending.Count == 0)
                root.Children.Add(NoteCard("Adaptadores", "Nenhuma alteração de economia de energia está aguardando restauração."));
            foreach (var backup in pending)
            {
                root.Children.Add(ActionCard(backup.Name,
                    "Selective Suspend original: " + backup.SelectiveSuspend +
                    "\nDevice Sleep on Disconnect original: " + backup.DeviceSleepOnDisconnect,
                    "Restaurar energia original", async () =>
                    {
                        if (MessageBox.Show("Restaurar as opções originais de energia do adaptador \"" + backup.Name + "\"? Pode ser solicitado UAC.",
                            "Confirmar restauração", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                            return;
                        await Task.Run(() => manager.Restore(backup.Name));
                        MessageBox.Show("Energia original do adaptador restaurada.", "ynoez", MessageBoxButton.OK, MessageBoxImage.Information);
                        ShowPage("Restore");
                    }, "BACKUP"));
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("Central de restauração dos adaptadores falhou: " + ex);
            root.Children.Add(NoteCard("Backups dos adaptadores precisam de atenção", ex.Message));
        }

        root.Children.Add(Heading("Tarefas agendadas opcionais", 15));
        try
        {
            var manager = new ScheduledTaskTweakService();
            var pending = manager.GetStatuses().Where(x => x.DisabledByYnoez).ToArray();
            if (pending.Length == 0)
                root.Children.Add(NoteCard("Tarefas", "Nenhuma tarefa desativada pelo ynoez está aguardando restauração."));
            foreach (var task in pending)
            {
                root.Children.Add(ActionCard(task.Definition.DisplayName,
                    task.Definition.Description + "\nEstado atual: " + task.State,
                    "Restaurar tarefa", async () =>
                    {
                        if (MessageBox.Show("Reativar a tarefa \"" + task.Definition.TaskName + "\" ao estado salvo?",
                            "Confirmar restauração", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                            return;
                        await Task.Run(() => manager.Restore(task.Definition));
                        MessageBox.Show("Tarefa reativada e verificada.", "ynoez", MessageBoxButton.OK, MessageBoxImage.Information);
                        ShowPage("Restore");
                    }, "BACKUP"));
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("Central de restauração de tarefas falhou: " + ex);
            root.Children.Add(NoteCard("Backups de tarefas precisam de atenção", ex.Message));
        }

        root.Children.Add(Heading("Inicialização", 15));
        try
        {
            var manager = new StartupManagerService();
            var pending = manager.GetDisabledEntries();
            if (pending.Count == 0)
                root.Children.Add(NoteCard("Inicialização", "Nenhuma entrada está aguardando restauração."));
            foreach (var entry in pending)
            {
                var source = entry.IsFile ? entry.OriginalFilePath ?? entry.Command : entry.Command;
                root.Children.Add(ActionCard(entry.Name, source,
                    entry.IsFile ? "Restaurar arquivo" : "Restaurar entrada", () =>
                    {
                        if (MessageBox.Show("Restaurar \"" + entry.Name + "\" à inicialização? O destino será validado antes da operação.",
                            "Confirmar restauração", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                            return Task.CompletedTask;
                        manager.Restore(entry);
                        MessageBox.Show("Entrada de inicialização restaurada.", "ynoez", MessageBoxButton.OK, MessageBoxImage.Information);
                        ShowPage("Restore");
                        return Task.CompletedTask;
                    }, "BACKUP"));
            }
        }
        catch (InvalidDataException ex)
        {
            AppLog.Write("Manifesto de inicialização requer recuperação: " + ex);
            root.Children.Add(NoteCard("Manifesto de inicialização corrompido — backups preservados", ex.Message));
        }
        catch (Exception ex)
        {
            AppLog.Write("Central de restauração da inicialização falhou: " + ex);
            root.Children.Add(NoteCard("Inicialização", ex.Message));
        }

        var backups = BackupService.ListBackups();
        var listCard = MakeCard();
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(Heading("Snapshots locais", 15));
        panel.Children.Add(Body("Local: " + AppPaths.Backups));
        panel.Children.Add(Body("Snapshots guardam inventário e hashes de integridade. Eles não são uma imagem do Windows e não substituem um backup externo."));
        if (backups.Count == 0) panel.Children.Add(Body("Nenhum snapshot criado ainda."));
        else panel.Children.Add(Body("Selecione um snapshot para calcular novamente os hashes e verificar se os arquivos continuam íntegros."));
        listCard.Child = panel;
        root.Children.Add(listCard);
        foreach (var path in backups.Take(10))
        {
            var created = Directory.GetCreationTime(path).ToString("dd/MM/yyyy HH:mm");
            root.Children.Add(ActionCard(Path.GetFileName(path), "Criado em " + created +
                ". O teste compara tamanho, SHA-256 e a presença de arquivos/pastas fora do manifesto. Não restaura o Windows.",
                "Verificar integridade", async () =>
                {
                    var result = await BackupService.VerifyLocalSnapshotAsync(path);
                    MessageBox.Show(result.Details, result.IsValid ? "Snapshot íntegro" : "Snapshot não validado",
                        MessageBoxButton.OK, result.IsValid ? MessageBoxImage.Information : MessageBoxImage.Warning);
                }, "SHA-256"));
        }
        root.Children.Add(ActionCard("Abrir pasta de backups", "Abre a pasta de snapshots e arquivos de recuperação locais do ynoez.", "Abrir pasta", () => OpenTargetAsync(AppPaths.Backups), "LOCAL"));
        return root;
    }

    private UIElement BuildLogs()
    {
        var root = PageContainer();
        root.Children.Add(PageIntro("Logs", "Histórico local das operações executadas pelo ynoez."));
        var card = MakeCard();
        var stack = new StackPanel { Margin = new Thickness(18) };
        stack.Children.Add(Heading("Atividade recente", 15));
        stack.Children.Add(Body("Arquivo: " + AppPaths.Logs));
        var text = new TextBox
        {
            Text = AppLog.ReadRecent(), IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = BrushFrom("#050505"),
            Foreground = SecondaryBrush, BorderBrush = BorderBrushSoft, FontFamily = new FontFamily("Consolas"),
            FontSize = 10, Height = 390, Padding = new Thickness(10), Margin = new Thickness(0, 12, 0, 0)
        };
        stack.Children.Add(text);
        var open = SmallAction("Abrir pasta de logs", () => OpenTargetAsync(AppPaths.Logs));
        stack.Children.Add(open);
        card.Child = stack;
        root.Children.Add(card);
        return root;
    }

    private UIElement ProtectedItemsCard()
    {
        var card = MakeCard();
        var stack = new StackPanel { Margin = new Thickness(20) };
        stack.Children.Add(Heading("Componentes preservados", 15));
        stack.Children.Add(Body("Estes itens ficam fora da remoção automática e das rotinas agressivas do ynoez."));
        var items = new[]
        {
            "Microsoft Store e serviços de licença/instalação", "Spooler, impressoras e recursos de impressão",
            "Calculadora, Clipboard e Bloco de Notas", "Windows Search e pesquisa pelo menu Iniciar",
            "Windows Update, BITS e serviços de atualização", "Windows Defender, SmartScreen e UAC",
            "TPM, Secure Boot, VBS/HVCI e IOMMU/DMA", "NVIDIA Instant Replay / overlay Alt+Z", "VALORANT Vanguard e FACEIT Anti-Cheat"
        };
        foreach (var item in items)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            row.Children.Add(new TextBlock { Text = "✓", Foreground = AccentBrush, FontSize = 13, Margin = new Thickness(0, 0, 9, 0) });
            row.Children.Add(Body(item));
            stack.Children.Add(row);
        }
        card.Child = stack;
        return card;
    }

    private static UIElement Bullet(string text, string marker = "✓")
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) };
        row.Children.Add(new TextBlock { Text = marker, Foreground = AccentBrush, FontSize = 12, Margin = new Thickness(0, 0, 9, 0), Width = 12 });
        row.Children.Add(Body(text));
        return row;
    }

    private static StackPanel PageContainer() => new() { Orientation = Orientation.Vertical, MinWidth = 0 };

    private static UIElement PageIntro(string title, string description)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 17) };
        stack.Children.Add(Heading(title, 21));
        stack.Children.Add(Body(description));
        return stack;
    }

    private static Border InfoTile(string title, string value, string symbol)
    {
        var border = MakeCard();
        border.Margin = new Thickness(0, 0, 11, 11);
        border.MinHeight = 120;
        var stack = new StackPanel { Margin = new Thickness(15, 14, 15, 14) };
        var top = new DockPanel();
        top.Children.Add(new TextBlock { Text = title, FontSize = 11, Foreground = SecondaryBrush, FontWeight = FontWeights.SemiBold });
        var icon = new TextBlock { Text = symbol, FontSize = 15, Foreground = AccentBrush, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(icon, Dock.Right);
        top.Children.Add(icon);
        stack.Children.Add(top);
        stack.Children.Add(new TextBlock
        {
            Text = value, FontSize = 12, Foreground = SecondaryBrush, Margin = new Thickness(0, 15, 0, 0),
            TextWrapping = TextWrapping.Wrap, MaxHeight = 48, ToolTip = value
        });
        border.Child = stack;
        return border;
    }

    private static Border ActionCard(string title, string description, string buttonText, Func<Task> action, string status)
    {
        var card = MakeCard();
        card.Margin = new Thickness(0, 0, 0, 11);
        var grid = new Grid { Margin = new Thickness(19, 15, 18, 15) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var header = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        header.Children.Add(Heading(title, 13));
        header.Children.Add(Badge(status, status.Contains("PROTEG") ? "#D2D2D2" : "#FFFFFF"));
        content.Children.Add(header);
        content.Children.Add(Body(description));
        grid.Children.Add(content);
        var button = new Button { Content = buttonText, Style = (Style)Application.Current.MainWindow.FindResource("AccentButton"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0), MinWidth = 116 };
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try { await action(); }
            catch (Exception ex)
            {
                AppLog.Write($"Ação '{title}' falhou: {ex}");
                MessageBox.Show(ex.Message, "ynoez — falha na operação", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { button.IsEnabled = true; }
        };
        Grid.SetColumn(button, 1);
        grid.Children.Add(button);
        card.Child = grid;
        return card;
    }

    private static Border NoteCard(string title, string description)
    {
        var card = MakeCard();
        card.Margin = new Thickness(0, 3, 0, 11);
        var stack = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };
        stack.Children.Add(Heading(title, 13));
        stack.Children.Add(Body(description));
        card.Child = stack;
        return card;
    }

    private static Border MakeCard() => new()
    {
        Background = CardBrush,
        BorderBrush = BorderBrushSoft,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        SnapsToDevicePixels = true
    };

    private static TextBlock Heading(string text, double size) => new()
    {
        Text = text, FontSize = size, FontWeight = FontWeights.SemiBold,
        Foreground = PrimaryBrush, TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 6)
    };

    private static TextBlock Body(string text) => new()
    {
        Text = text, FontSize = 10.5, Foreground = SecondaryBrush,
        TextWrapping = TextWrapping.Wrap, LineHeight = 17, Margin = new Thickness(0, 1, 0, 0)
    };

    private static Border Badge(string text, string color)
    {
        var brush = BrushFrom(color);
        return new Border
        {
            Background = BrushFrom("#151515"), CornerRadius = new CornerRadius(4),
            Padding = new Thickness(7, 3, 7, 3), Margin = new Thickness(10, 0, 0, 4),
            Child = new TextBlock { Text = text, Foreground = brush, FontSize = 8, FontWeight = FontWeights.Bold }
        };
    }

    private static UIElement KeyValue(string label, string value)
    {
        var grid = new Grid { Margin = new Thickness(0, 9, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(Body(label));
        var valueText = Body(value);
        valueText.Foreground = PrimaryBrush;
        Grid.SetColumn(valueText, 1);
        grid.Children.Add(valueText);
        return grid;
    }

    private static Button SmallAction(string text, Action action)
    {
        var button = new Button { Content = text, Style = (Style)Application.Current.MainWindow.FindResource("QuietButton"), Margin = new Thickness(0, 0, 9, 7) };
        button.Click += (_, _) =>
        {
            try { action(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "ynoez", MessageBoxButton.OK, MessageBoxImage.Warning); }
        };
        return button;
    }

    private static string ReadServiceState(string serviceName)
    {
        try
        {
            var escapedServiceName = serviceName.Replace("'", "''");
            using var searcher = new System.Management.ManagementObjectSearcher(
                $"SELECT Name, State, StartMode FROM Win32_Service WHERE Name='{escapedServiceName}'");
            foreach (System.Management.ManagementObject item in searcher.Get())
            {
                using (item)
                {
                    var state = item["State"]?.ToString() ?? "Desconhecido";
                    var start = item["StartMode"]?.ToString() ?? "Desconhecido";
                    return $"Estado atual: {state} • Inicialização: {start}";
                }
            }
            return "Estado atual: serviço não encontrado nesta instalação do Windows.";
        }
        catch (Exception ex)
        {
            AppLog.Write($"Falha ao consultar serviço {serviceName}: {ex.Message}");
            return "Não foi possível consultar o estado deste serviço.";
        }
    }

    private static Task OpenTargetAsync(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            AppLog.Write("Destino aberto: " + target);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Falha ao abrir '{target}': {ex.Message}");
            MessageBox.Show("Não foi possível abrir este destino.\n\n" + ex.Message, "ynoez", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        return Task.CompletedTask;
    }

    private static Brush BrushFrom(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        DependencyObject? current = e.OriginalSource as DependencyObject;
        while (current is not null)
        {
            if (current is Button) return;
            current = VisualTreeHelper.GetParent(current);
        }
        DragMove();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose && !_sessionEnding && !Environment.HasShutdownStarted && HasBackgroundWork())
        {
            if (!EnsureTrayIcon())
            {
                AppLog.Write("Não foi possível criar o ícone da bandeja; a janela não será ocultada.");
                return;
            }

            e.Cancel = true;
            Hide();
            AppLog.Write("Janela ocultada na bandeja; operações em segundo plano continuam ativas.");
            return;
        }

        RemoveTrayIcon();
    }

    private void Application_SessionEnding(object? sender, SessionEndingCancelEventArgs e) =>
        _sessionEnding = true;

    private bool HasBackgroundWork() =>
        _networkMonitorCts is not null ||
        _networkDiagnosticCts is not null ||
        _appxInventoryCts is not null ||
        _systemAppxInventoryCts is not null ||
        _programInventoryCts is not null ||
        _autoProfileCts is not null ||
        _metricsCts is not null ||
        _benchmarkCts is not null ||
        _integrityAuditCts is not null ||
        _windowsRepairCts is not null ||
        ProcessPriorityProfileService.LoadProfile().Enabled;

    private bool EnsureTrayIcon()
    {
        if (_trayIconVisible) return true;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return false;

        _windowSource ??= HwndSource.FromHwnd(handle);
        if (_windowSource is null)
        {
            AppLog.Write("Não foi possível obter a origem da janela para registrar os eventos da bandeja.");
            return false;
        }
        if (!_trayHookAttached)
        {
            _windowSource.AddHook(TrayWindowProc);
            _trayHookAttached = true;
        }

        var icon = LoadSystemIcon(IntPtr.Zero, new IntPtr(32512));
        if (icon == IntPtr.Zero)
        {
            AppLog.Write("O Windows não forneceu um ícone padrão para a bandeja.");
            return false;
        }

        var data = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            WindowHandle = handle,
            Id = 1,
            Flags = TrayNifMessage | TrayNifIcon | TrayNifTip,
            CallbackMessage = TrayCallbackMessage,
            Icon = icon,
            ToolTip = "ynoez — operações continuam em segundo plano",
            Info = string.Empty,
            InfoTitle = string.Empty,
            ItemGuid = Guid.Empty
        };

        if (!ShellNotifyIcon(TrayNimAdd, ref data))
        {
            AppLog.Write("Shell_NotifyIcon falhou ao registrar a bandeja. Win32=" + Marshal.GetLastWin32Error());
            return false;
        }

        _trayIconVisible = true;
        return true;
    }

    private void RemoveTrayIcon()
    {
        if (!_trayIconVisible) return;
        var handle = new WindowInteropHelper(this).Handle;
        var data = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            WindowHandle = handle,
            Id = 1,
            ToolTip = string.Empty,
            Info = string.Empty,
            InfoTitle = string.Empty,
            ItemGuid = Guid.Empty
        };
        ShellNotifyIcon(TrayNimDelete, ref data);
        _trayIconVisible = false;
    }

    private IntPtr TrayWindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == TaskbarCreatedMessage)
        {
            if (_trayIconVisible)
            {
                _trayIconVisible = false;
                if (!EnsureTrayIcon())
                    AppLog.Write("O Explorer reiniciou e o ícone do ynoez não pôde ser registrado novamente.");
            }
            handled = true;
            return IntPtr.Zero;
        }

        if (message != TrayCallbackMessage || !_trayIconVisible)
            return IntPtr.Zero;

        var notification = unchecked((int)lParam.ToInt64());
        if (notification == WmLeftButtonDoubleClick)
        {
            handled = true;
            Dispatcher.BeginInvoke(new Action(RestoreFromTray));
        }
        else if (notification == WmRightButtonUp)
        {
            handled = true;
            Dispatcher.BeginInvoke(new Action(ShowTrayMenu));
        }
        return IntPtr.Zero;
    }

    private void ShowTrayMenu()
    {
        var menu = new ContextMenu
        {
            Placement = PlacementMode.MousePoint,
            StaysOpen = false
        };
        var status = new MenuItem
        {
            Header = HasBackgroundWork() ? "ynoez está trabalhando em segundo plano" : "ynoez está aberto",
            IsEnabled = false
        };
        var open = new MenuItem { Header = "Abrir ynoez" };
        open.Click += (_, _) => RestoreFromTray();
        var exit = new MenuItem { Header = "Encerrar ynoez e interromper operações" };
        exit.Click += (_, _) => ExitFromTray();
        menu.Items.Add(status);
        menu.Items.Add(new Separator());
        menu.Items.Add(open);
        menu.Items.Add(exit);
        SetForegroundWindow(new WindowInteropHelper(this).Handle);
        menu.IsOpen = true;
    }

    private void RestoreFromTray()
    {
        RemoveTrayIcon();
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Focus();
    }

    private void ExitFromTray()
    {
        _allowClose = true;
        RemoveTrayIcon();
        Close();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
