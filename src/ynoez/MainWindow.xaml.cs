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
            TimerResolutionService.Disable();
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

    private void Login_Click(object sender, RoutedEventArgs e)
    {
        // This is a local visual gate only. It deliberately does not authenticate,
        // store credentials, or transmit any entered text.
        LoginOverlay.Visibility = Visibility.Collapsed;
        AppLog.Write("Acesso local iniciado; o painel de login não valida credenciais.");
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var page = button.Name switch
        {
            "NavHome" => "Home",
            "NavTweaks" => "Tweaks",
            "NavDebloat" => "Debloat",
            "NavProcessFocus" => "Foco de Jogo",
            "NavBoard" => "Motherboard",
            _ => "Home"
        };
        ShowPage(page);
    }

    private void ShowPage(string page)
    {
        _currentPage = page;
        PageTitle.Text = page switch
        {
            "Tweaks" => "Otimizações",
            "Motherboard" => "BIOS / Placa-mãe",
            "Foco de Jogo" => "Foco de Jogo",
            _ => page
        };
        SetNavigationState(page);
        PageScroll.ScrollToTop();
        PageHost.Content = page switch
        {
            "Home" => BuildHome(),
            "Foco de Jogo" => BuildProcessFocusPage(),
            "Backup" => BuildBackup(),
            "Tweaks" => BuildTweaks(),
            "Debloat" => BuildDebloat(),
            "Motherboard" => BuildMotherboard(),
            _ => BuildHome()
        };
    }

    private void SetNavigationState(string page)
    {
        var map = new Dictionary<string, Button>
        {
            ["Home"] = NavHome,
            ["Tweaks"] = NavTweaks,
            ["Debloat"] = NavDebloat,
            ["Foco de Jogo"] = NavProcessFocus,
            ["Motherboard"] = NavBoard,
        };
        foreach (var item in map)
        {
            item.Value.Background = item.Key == page ? BrushFrom("#1D1D1D") : PageBrush;
            item.Value.Foreground = PrimaryBrush;
        }
    }

    private UIElement BuildHome()
    {
        var root = PageContainer();
        root.Children.Add(PageIntro("Home", "Seu PC. Seus ajustes. Sem painel de gráficos."));

        var hardware = new UniformGrid { Columns = 3, Margin = new Thickness(0, 2, 0, 15) };
        hardware.Children.Add(InfoTile("CPU", _hardware.Cpu, "CPU"));
        hardware.Children.Add(InfoTile("GPU", _hardware.Gpu, "GPU"));
        hardware.Children.Add(InfoTile("RAM", _hardware.Memory, "RAM"));
        root.Children.Add(hardware);

        var actions = MakeCard();
        var actionPanel = new StackPanel { Margin = new Thickness(17, 14, 17, 14) };
        actionPanel.Children.Add(Heading("Ações", 14));
        var actionGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 5, 0, 0) };
        actionGrid.Children.Add(SmallAction("Otimizações", () => ShowPage("Tweaks")));
        actionGrid.Children.Add(SmallAction("Debloat", () => ShowPage("Debloat")));
        actionGrid.Children.Add(SmallAction("Foco de Jogo", () => ShowPage("Foco de Jogo")));
        actionGrid.Children.Add(SmallAction("Placa-mãe", () => ShowPage("Motherboard")));
        actionPanel.Children.Add(actionGrid);
        actions.Child = actionPanel;
        root.Children.Add(actions);

        var system = Body(_hardware.OperatingSystem + " • " + _hardware.BoardProduct);
        system.Margin = new Thickness(2, 11, 0, 0);
        system.Foreground = MutedBrush;
        root.Children.Add(system);
        return root;
    }

    private Border BuildToggleCard(
        string title, string description, bool isChecked,
        Func<bool, Task> onChanged, bool canChange = true, string state = "")
    {
        var card = MakeCard();
        card.Margin = new Thickness(0, 0, 9, 9);
        card.MinHeight = 112;
        var panel = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleText = Heading(title, 12);
        titleText.Margin = new Thickness(0, 0, 8, 0);
        header.Children.Add(titleText);
        var toggle = new ToggleButton
        {
            Style = (Style)FindResource("SwitchToggle"),
            IsChecked = isChecked,
            IsEnabled = canChange,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(toggle, 1);
        header.Children.Add(toggle);
        panel.Children.Add(header);

        var detail = Body(description);
        detail.FontSize = 9;
        detail.Margin = new Thickness(0, 5, 0, 0);
        detail.MaxHeight = 34;
        panel.Children.Add(detail);

        var status = Body(state);
        status.FontSize = 8;
        status.Foreground = MutedBrush;
        status.Margin = new Thickness(0, 7, 0, 0);
        panel.Children.Add(status);

        var busy = false;
        var internalChange = false;
        async Task ChangeAsync(bool requested)
        {
            if (busy || internalChange) return;
            busy = true;
            toggle.IsEnabled = false;
            status.Text = requested ? "APLICANDO…" : "RESTAURANDO…";
            try
            {
                await onChanged(requested);
                status.Text = requested ? "ATIVO" : "PADRÃO";
            }
            catch (Exception ex)
            {
                AppLog.Write("Alternância de '" + title + "' falhou: " + ex);
                internalChange = true;
                toggle.IsChecked = !requested;
                internalChange = false;
                status.Text = "FALHOU";
                MessageBox.Show(ex.Message, "ynoez", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                busy = false;
                toggle.IsEnabled = canChange;
            }
        }
        toggle.Checked += async (_, _) => await ChangeAsync(true);
        toggle.Unchecked += async (_, _) => await ChangeAsync(false);
        if (!canChange && string.IsNullOrWhiteSpace(state))
            status.Text = isChecked ? "ATIVO NO WINDOWS" : "INDISPONÍVEL";

        card.Child = panel;
        return card;
    }

    private Border BuildToolActionCard(string title, string description, string buttonText, Func<Task> action)
    {
        var card = MakeCard();
        card.Margin = new Thickness(0, 0, 9, 9);
        card.MinHeight = 112;
        var panel = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
        panel.Children.Add(Heading(title, 12));
        var detail = Body(description);
        detail.FontSize = 9;
        detail.Margin = new Thickness(0, 2, 0, 6);
        detail.MaxHeight = 30;
        panel.Children.Add(detail);
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var status = Body("PRONTO");
        status.FontSize = 8;
        status.Foreground = MutedBrush;
        status.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(status);
        var button = new Button
        {
            Content = buttonText,
            Style = (Style)FindResource("QuietButton"),
            Padding = new Thickness(12, 6, 12, 6)
        };
        Grid.SetColumn(button, 1);
        row.Children.Add(button);
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            status.Text = "EXECUTANDO…";
            try
            {
                await action();
                status.Text = "CONCLUÍDO";
            }
            catch (Exception ex)
            {
                status.Text = "FALHOU";
                AppLog.Write("Ferramenta '" + title + "' falhou: " + ex);
                MessageBox.Show(ex.Message, "ynoez", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally { button.IsEnabled = true; }
        };
        panel.Children.Add(row);
        card.Child = panel;
        return card;
    }

    private static async Task RunLocalProcessAsync(string executable, string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException(executable + " excedeu 30 segundos.");
        }
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(executable + " terminou com código " + process.ExitCode + ". " + error + " " + output);
        if (!string.IsNullOrWhiteSpace(output))
            AppLog.Write(executable + ": " + output.Trim());
    }

    private static Task OpenAmdSoftwareAsync()
    {
        var path = @"C:\Program Files\AMD\CNext\CNext\RadeonSoftware.exe";
        return File.Exists(path)
            ? OpenTargetAsync(path)
            : OpenTargetAsync("ms-settings:display-advancedgraphics");
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

    private UIElement BuildTweaks() => BuildPerformancePage();

    private Border BuildRegistryToggleCard(RegistryTweak tweak, RegistryTweakService service)
    {
        var state = service.Inspect(tweak);
        var hasBackup = service.CanRestore(tweak.Id);
        var enabled = state == TweakState.Enabled || hasBackup;
        var canChange = state != TweakState.Enabled || hasBackup;
        var status = hasBackup ? "BACKUP SALVO" : enabled ? "ATIVO NO WINDOWS" : "PADRÃO";
        return BuildToggleCard(tweak.Name, tweak.Description, enabled, async requested =>
        {
            await Task.Run(() =>
            {
                if (requested) service.Apply(tweak);
                else service.Restore(tweak);
            });
        }, canChange, status);
    }

    private Border BuildSystemRegistryToggleCard(string id)
    {
        var status = SystemRegistryTweakService.GetStatus(id);
        return BuildToggleCard(
            status.Definition.Name,
            status.Definition.Description,
            status.IsApplied,
            requested => Task.Run(() =>
            {
                if (requested) SystemRegistryTweakService.Apply(id);
                else SystemRegistryTweakService.Restore(id);
            }),
            status.CanChange,
            (status.HasBackup ? "BACKUP SALVO" : status.IsApplied ? "ATIVO NO WINDOWS" : "PADRÃO") +
            (status.Definition.RequiresRestart ? " • REINICIAR" : ""));
    }

    private Border BuildTimerResolutionCard()
    {
        return BuildToggleCard(
            "Temporizador 1 ms",
            "Solicita resolução de timer apenas para o processo ynoez. Pode aumentar consumo; o Windows 11 pode ignorar quando a janela fica oculta.",
            TimerResolutionService.IsEnabled,
            enabled => Task.Run(() =>
            {
                if (enabled) TimerResolutionService.Enable();
                else TimerResolutionService.Disable();
            }),
            true,
            TimerResolutionService.IsEnabled ? "ATIVO NESTA SESSÃO" : "DESLIGADO");
    }

    private Task SetAutomaticProfilesAsync(bool enabled, TextBlock status)
    {
        if (enabled)
        {
            if (_autoProfileCts is not null) return Task.CompletedTask;
            if (QuickGameOptimizationService.IsApplied)
                throw new InvalidOperationException("Restaure o perfil rápido anterior antes de ativar a automação.");

            var cts = new CancellationTokenSource();
            _autoProfileCts = cts;
            status.Text = "Iniciando…";
            _ = RunAutomaticProfilesAsync(cts, status);
        }
        else
        {
            _autoProfileCts?.Cancel();
            status.Text = "Parando e restaurando o plano…";
        }
        return Task.CompletedTask;
    }

    private async Task RunAutomaticProfilesAsync(CancellationTokenSource cts, TextBlock status)
    {
        try
        {
            await AutoProfileService.RunAsync(value =>
            {
                if (!Dispatcher.HasShutdownStarted)
                    Dispatcher.Invoke(() => status.Text =
                        AutoProfileService.GetModeDisplayName(value.Mode) +
                        " • " + value.IdleTime.TotalMinutes.ToString("0") + " min sem atividade" +
                        (value.ActiveGame.Length > 0 ? " • " + value.ActiveGame : "") +
                        (value.ActiveProtectedProcess.Length > 0 ? " • " + value.ActiveProtectedProcess : ""));
            }, cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            if (!Dispatcher.HasShutdownStarted) status.Text = "Desligado";
        }
        catch (Exception ex)
        {
            AppLog.Write("Perfis automáticos falharam: " + ex);
            if (!Dispatcher.HasShutdownStarted) status.Text = "Falhou: " + ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_autoProfileCts, cts))
            {
                _autoProfileCts = null;
                cts.Dispose();
            }
        }
    }

    private UIElement BuildNetwork()
    {
        var root = PageContainer();
        root.Children.Add(PageIntro("Rede", "Ajuste de energia do adaptador e limpeza rápida de DNS."));

        var tools = new UniformGrid { Columns = 2 };
        tools.Children.Add(BuildToolActionCard("Limpar cache DNS", "Executa ipconfig /flushdns.", "Executar", () => RunLocalProcessAsync("ipconfig.exe", "/flushdns")));
        tools.Children.Add(BuildToolActionCard("Conexões de rede", "Abre os adaptadores do Windows.", "Abrir", () => OpenTargetAsync("ncpa.cpl")));
        root.Children.Add(tools);

        var adapterCard = MakeCard();
        var panel = new StackPanel { Margin = new Thickness(16, 14, 16, 14) };
        panel.Children.Add(Heading("Energia dos adaptadores", 14));
        var status = Body("Consultando adaptadores…");
        panel.Children.Add(status);
        var adapters = new UniformGrid { Columns = 2, Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(adapters);
        adapterCard.Child = panel;
        root.Children.Add(adapterCard);
        _ = LoadNetworkAdaptersAsync(adapters, status);

        var latencyCard = MakeCard();
        var latencyPanel = new StackPanel { Margin = new Thickness(16, 14, 16, 14) };
        latencyPanel.Children.Add(Heading("Latência e RSS do driver", 14));
        var latencyStatus = Body("Lendo apenas as propriedades suportadas pelo driver…");
        latencyPanel.Children.Add(latencyStatus);
        var latencyGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 8, 0, 0) };
        latencyPanel.Children.Add(latencyGrid);
        latencyCard.Child = latencyPanel;
        root.Children.Add(latencyCard);
        _ = LoadNetworkPerformanceTweaksAsync(latencyGrid, latencyStatus);
        return root;
    }

    private async Task LoadNetworkAdaptersAsync(UniformGrid host, TextBlock status)
    {
        try
        {
            var manager = new AdapterPowerManagementService();
            var adapters = await Task.Run(manager.GetPhysicalAdapters);
            host.Children.Clear();
            var candidates = adapters.Where(x => manager.CanRestore(x.Name) || manager.CanReducePowerSaving(x)).ToArray();
            foreach (var adapter in candidates)
            {
                var hasBackup = manager.CanRestore(adapter.Name);
                var canReduce = manager.CanReducePowerSaving(adapter);
                host.Children.Add(BuildToggleCard(
                    adapter.Name,
                    adapter.InterfaceDescription + " • " + adapter.LinkSpeed,
                    hasBackup,
                    enabled => Task.Run(() =>
                    {
                        if (enabled) manager.ReducePowerSaving(adapter);
                        else manager.Restore(adapter.Name);
                    }),
                    hasBackup || canReduce,
                    hasBackup ? "BACKUP SALVO" : "ECONOMIA ATIVA"));
            }

            status.Text = candidates.Length == 0
                ? "Nenhum adaptador físico tem economia de energia que o ynoez possa alterar com segurança."
                : candidates.Length + " adaptador(es) com opção de economia disponível.";
        }
        catch (Exception ex)
        {
            AppLog.Write("Consulta de energia dos adaptadores falhou: " + ex);
            status.Text = "Falha ao consultar adaptadores: " + ex.Message;
        }
    }

    private async Task LoadNetworkPerformanceTweaksAsync(UniformGrid host, TextBlock status)
    {
        try
        {
            var candidates = await NetworkAdapterPerformanceTweakService.GetCandidatesAsync();
            host.Children.Clear();
            foreach (var item in candidates)
            {
                var description = item.DisplayName switch
                {
                    "Receive Side Scaling (RSS)" => "RSS distribui o trabalho de recepção entre núcleos.",
                    "Interrupt Moderation" => "Teste desligado para latência menor; pode aumentar CPU.",
                    "Energy Efficient Ethernet" => "Teste desligado se houver pausas/renegociação do link.",
                    _ => item.DisplayName
                };
                var state = item.HasConflict ? "ALTERADO FORA DO YNOEZ"
                    : item.HasBackup ? "BACKUP SALVO"
                    : item.IsAtTarget ? "JÁ OTIMIZADO"
                    : "PADRÃO DO DRIVER";
                var checkedState = item.HasBackup || item.IsAtTarget;
                var canChange = !item.HasConflict && (item.HasBackup || !item.IsAtTarget);
                host.Children.Add(BuildToggleCard(
                    item.AdapterName + " • " + item.DisplayName,
                    description + " • Atual: " + item.CurrentDisplayValue,
                    checkedState,
                    async enabled =>
                    {
                        if (enabled)
                            await NetworkAdapterPerformanceTweakService.ApplyAsync(item);
                        else
                            await NetworkAdapterPerformanceTweakService.RestoreAsync(item);
                        await LoadNetworkPerformanceTweaksAsync(host, status);
                    },
                    canChange,
                    state));
            }

            status.Text = candidates.Count == 0
                ? "Este driver não expõe RSS, Interrupt Moderation ou EEE em formato que o ynoez possa alterar com segurança."
                : candidates.Count + " opção(ões) disponíveis no(s) driver(s) instalado(s).";
        }
        catch (Exception ex)
        {
            AppLog.Write("Consulta de otimizações avançadas de NIC falhou: " + ex);
            status.Text = "Falha ao consultar propriedades de rede: " + ex.Message;
        }
    }

    private UIElement BuildDebloat() => BuildDebloatPage();

    private Border BuildAppxDebloatCard()
    {
        var card = MakeCard();
        var panel = new StackPanel { Margin = new Thickness(15) };
        panel.Children.Add(Heading("Aplicativos opcionais", 13));
        var status = Body("Analise para listar somente os aplicativos da lista permitida.");
        status.Margin = new Thickness(0, 5, 0, 8);
        var list = new ListBox { Height = 175, SelectionMode = SelectionMode.Extended };
        var buttons = new WrapPanel();
        var analyze = new Button
        {
            Content = "Analisar",
            Style = (Style)FindResource("AccentButton"),
            Margin = new Thickness(0, 0, 7, 0)
        };
        var cancel = new Button
        {
            Content = "Cancelar",
            Style = (Style)FindResource("QuietButton"),
            IsEnabled = false,
            Margin = new Thickness(0, 0, 7, 0)
        };
        var remove = new Button
        {
            Content = "Remover selecionados",
            Style = (Style)FindResource("QuietButton"),
            IsEnabled = false
        };

        async Task AnalyzeAsync()
        {
            if (_appxInventoryCts is not null) return;
            var cts = new CancellationTokenSource();
            _appxInventoryCts = cts;
            analyze.IsEnabled = false;
            cancel.IsEnabled = true;
            remove.IsEnabled = false;
            status.Text = "Analisando…";
            try
            {
                var packages = await AppPackageInventoryService.GetCurrentUserPackagesAsync(cts.Token);
                var candidates = AppxDebloatService.FindCandidates(packages);
                list.Items.Clear();
                foreach (var item in candidates)
                    list.Items.Add(new ListBoxItem
                    {
                        Content = item.DisplayName + " • " + item.Version,
                        Tag = item,
                        IsSelected = true
                    });
                status.Text = candidates.Count == 0
                    ? "Nenhum app opcional da lista foi encontrado."
                    : candidates.Count + " app(s) selecionado(s). Desmarque o que deseja manter.";
                remove.IsEnabled = candidates.Count > 0;
            }
            catch (OperationCanceledException)
            {
                status.Text = "Análise cancelada.";
            }
            catch (Exception ex)
            {
                AppLog.Write("Análise AppX falhou: " + ex);
                status.Text = "Falha: " + ex.Message;
            }
            finally
            {
                if (ReferenceEquals(_appxInventoryCts, cts)) _appxInventoryCts = null;
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
                .Select(item => item.Tag).OfType<AppxRemovalCandidate>().ToArray();
            if (selected.Length == 0) return;
            if (MessageBox.Show(
                "Remover " + selected.Length + " aplicativo(s) da conta atual? Esta remoção não tem restauração automática.",
                "Confirmar Debloat", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            if (_appxInventoryCts is not null) return;
            var cts = new CancellationTokenSource();
            _appxInventoryCts = cts;
            analyze.IsEnabled = false;
            cancel.IsEnabled = true;
            remove.IsEnabled = false;
            var removed = 0;
            var errors = new List<string>();
            try
            {
                foreach (var item in selected)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    status.Text = "Removendo " + item.DisplayName + "…";
                    try
                    {
                        await AppxDebloatService.RemoveCurrentUserPackageAsync(item, cts.Token);
                        removed++;
                    }
                    catch (Exception ex)
                    {
                        errors.Add(item.DisplayName + ": " + ex.Message);
                    }
                }
                status.Text = removed + " removido(s)" +
                    (errors.Count > 0 ? " • " + errors.Count + " falha(s): " + string.Join(" | ", errors.Take(2)) : "");
            }
            catch (OperationCanceledException)
            {
                status.Text = "Cancelado • " + removed + " removido(s).";
            }
            finally
            {
                if (ReferenceEquals(_appxInventoryCts, cts)) _appxInventoryCts = null;
                cts.Dispose();
                analyze.IsEnabled = true;
                cancel.IsEnabled = false;
            }
            if (removed > 0) await AnalyzeAsync();
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

    private async Task LoadWin32ProgramsAsync(StackPanel host, TextBlock status)
    {
        try
        {
            var programs = await Task.Run(Win32ProgramRemovalService.GetCandidates);
            host.Children.Clear();
            status.Text = programs.Count + " programa(s) com desinstalador direto válido.";
            host.Children.Add(status);

            var list = new ListBox { Height = 240, Margin = new Thickness(0, 8, 0, 8) };
            foreach (var item in programs)
            {
                var version = string.IsNullOrWhiteSpace(item.Version) ? "" : " • " + item.Version;
                list.Items.Add(new ListBoxItem
                {
                    Content = item.DisplayName + version + Environment.NewLine +
                        (string.IsNullOrWhiteSpace(item.Publisher) ? item.HiveName : item.Publisher + " • " + item.HiveName),
                    Tag = item
                });
            }

            var row = new WrapPanel();
            var uninstall = new Button
            {
                Content = "Desinstalar selecionado",
                Style = (Style)FindResource("AccentButton"),
                IsEnabled = false
            };
            var refresh = new Button
            {
                Content = "Atualizar lista",
                Style = (Style)FindResource("QuietButton"),
                Margin = new Thickness(8, 0, 0, 0)
            };
            list.SelectionChanged += (_, _) => uninstall.IsEnabled = list.SelectedItem is ListBoxItem;
            uninstall.Click += async (_, _) =>
            {
                if (list.SelectedItem is not ListBoxItem selected ||
                    selected.Tag is not Win32UninstallCandidate candidate)
                    return;

                if (MessageBox.Show(
                    "Abrir o desinstalador real de \"" + candidate.DisplayName + "\"?" +
                    Environment.NewLine + "Editor: " + candidate.Publisher +
                    Environment.NewLine + "O instalador poderá solicitar UAC. O ynoez não executa comandos por cmd/PowerShell.",
                    "Confirmar desinstalação", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;

                uninstall.IsEnabled = false;
                status.Text = "Iniciando desinstalador…";
                try
                {
                    var result = await Task.Run(() => Win32ProgramRemovalService.LaunchUninstaller(candidate));
                    status.Text = "Desinstalador aberto: " + result.DisplayName;
                    AppLog.Write("Desinstalador Win32 iniciado: " + result.DisplayName);
                }
                catch (Exception ex)
                {
                    status.Text = "Não foi possível iniciar: " + ex.Message;
                    AppLog.Write("Desinstalação Win32 falhou: " + ex);
                }
                finally { uninstall.IsEnabled = list.SelectedItem is ListBoxItem; }
            };
            refresh.Click += async (_, _) =>
            {
                refresh.IsEnabled = false;
                status.Text = "Atualizando…";
                try { await LoadWin32ProgramsAsync(host, status); }
                finally { refresh.IsEnabled = true; }
            };
            row.Children.Add(uninstall);
            row.Children.Add(refresh);
            host.Children.Add(list);
            host.Children.Add(row);
        }
        catch (Exception ex)
        {
            AppLog.Write("Consulta Win32 para desinstalação falhou: " + ex);
            status.Text = "Falha ao ler programas Win32: " + ex.Message;
        }
    }

    private async Task LoadDebloatStartupAsync(
        StackPanel host, TextBlock status, StartupManagerService manager)
    {
        try
        {
            var snapshot = await Task.Run(() => new
            {
                Active = manager.GetActiveEntries().Where(entry => entry.CanDisable).Take(18).ToArray(),
                Disabled = manager.GetDisabledEntries().ToArray()
            });

            host.Children.Clear();
            status.Text = snapshot.Active.Length + " entrada(s) ativa(s) • " +
                snapshot.Disabled.Length + " backup(s) • somente inicialização do usuário.";
            host.Children.Add(status);

            var activeGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 7, 0, 10) };
            foreach (var entry in snapshot.Active)
                activeGrid.Children.Add(BuildToggleCard(
                    entry.Name,
                    "Inicialização do usuário",
                    false,
                    async enabled =>
                    {
                        if (!enabled) return;
                        await Task.Run(() => manager.Disable(entry));
                        if (_currentPage == "Debloat") ShowPage("Debloat");
                    },
                    true,
                    "ATIVO"));
            host.Children.Add(activeGrid);

            if (snapshot.Disabled.Length > 0)
            {
                host.Children.Add(Heading("Desativados pelo ynoez", 12));
                var restoreGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 5, 0, 0) };
                foreach (var entry in snapshot.Disabled)
                    restoreGrid.Children.Add(BuildToggleCard(
                        entry.Name,
                        "Backup de inicialização",
                        true,
                        async enabled =>
                        {
                            if (enabled) return;
                            await Task.Run(() => manager.Restore(entry));
                            if (_currentPage == "Debloat") ShowPage("Debloat");
                        },
                        true,
                        "BACKUP"));
                host.Children.Add(restoreGrid);
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("Leitura de inicialização falhou: " + ex);
            status.Text = "Falha ao ler inicialização: " + ex.Message;
        }
    }

    private async Task LoadDebloatServicesAsync(
        StackPanel host, TextBlock status, ServiceTweakService manager)
    {
        try
        {
            var values = await Task.Run(manager.GetStatuses);
            host.Children.Clear();
            status.Text = values.Count + " serviço(s) opcional(is) detectado(s).";
            host.Children.Add(status);
            var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 7, 0, 0) };
            foreach (var item in values.Where(x => x.Available || x.DisabledByYnoez))
            {
                var isOff = item.DisabledByYnoez ||
                    string.Equals(item.StartMode, "Disabled", StringComparison.OrdinalIgnoreCase);
                var canChange = item.DisabledByYnoez ||
                    (item.Available && !string.Equals(item.StartMode, "Disabled", StringComparison.OrdinalIgnoreCase));
                grid.Children.Add(BuildToggleCard(
                    item.Definition.DisplayName,
                    item.Definition.Risk + " • " + item.State,
                    isOff,
                    async enabled =>
                    {
                        await Task.Run(() =>
                        {
                            if (enabled) manager.Disable(item.Definition);
                            else manager.Restore(item.Definition);
                        });
                        if (_currentPage == "Debloat") ShowPage("Debloat");
                    },
                    canChange,
                    item.DisabledByYnoez ? "BACKUP" : isOff ? "JÁ DESATIVADO" : "ATIVO"));
            }
            host.Children.Add(grid);
        }
        catch (Exception ex)
        {
            AppLog.Write("Leitura de serviços opcionais falhou: " + ex);
            status.Text = "Falha ao consultar serviços: " + ex.Message;
        }
    }

    private async Task LoadDebloatTasksAsync(
        StackPanel host, TextBlock status, ScheduledTaskTweakService manager)
    {
        try
        {
            var values = await Task.Run(manager.GetStatuses);
            host.Children.Clear();
            status.Text = values.Count + " tarefa(s) opcionais detectada(s).";
            host.Children.Add(status);
            var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 7, 0, 0) };
            foreach (var item in values.Where(x => x.Available || x.DisabledByYnoez))
            {
                var isOff = item.DisabledByYnoez ||
                    string.Equals(item.State, "Disabled", StringComparison.OrdinalIgnoreCase);
                var canChange = item.DisabledByYnoez ||
                    (item.Available && string.Equals(item.State, "Ready", StringComparison.OrdinalIgnoreCase));
                grid.Children.Add(BuildToggleCard(
                    item.Definition.DisplayName,
                    item.Definition.Risk + " • " + item.State,
                    isOff,
                    async enabled =>
                    {
                        await Task.Run(() =>
                        {
                            if (enabled) manager.Disable(item.Definition);
                            else manager.Restore(item.Definition);
                        });
                        if (_currentPage == "Debloat") ShowPage("Debloat");
                    },
                    canChange,
                    item.DisabledByYnoez ? "BACKUP" : isOff ? "JÁ DESATIVADA" : "ATIVA"));
            }
            host.Children.Add(grid);
        }
        catch (Exception ex)
        {
            AppLog.Write("Leitura de tarefas opcionais falhou: " + ex);
            status.Text = "Falha ao consultar tarefas: " + ex.Message;
        }
    }

    private UIElement BuildMotherboard() => BuildFirmwarePage();

    private Task RestartToUefiAsync()
    {
        if (MessageBox.Show(
            "O Windows será reiniciado diretamente na UEFI/BIOS. Salve seus arquivos antes de continuar.",
            "Entrar na UEFI", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return Task.CompletedTask;

        Process.Start(new ProcessStartInfo("shutdown.exe", "/r /fw /t 0") { UseShellExecute = true });
        return Task.CompletedTask;
    }

    private UIElement BuildToolbox()
    {
        var root = PageContainer();
        root.Children.Add(PageIntro("Toolbox", "Atalhos diretos para as ferramentas do Windows e do driver."));
        var grid = new UniformGrid { Columns = 3 };
        grid.Children.Add(BuildToolActionCard("Limpar DNS", "Executa ipconfig /flushdns.", "Executar", () =>
            RunLocalProcessAsync("ipconfig.exe", "/flushdns")));
        grid.Children.Add(BuildToolActionCard("Serviços", "Abre services.msc.", "Abrir", () => OpenTargetAsync("services.msc")));
        grid.Children.Add(BuildToolActionCard("Gerenciador de tarefas", "Abre Task Manager.", "Abrir", () => OpenTargetAsync("taskmgr.exe")));
        grid.Children.Add(BuildToolActionCard("Gerenciador de dispositivos", "Abre drivers e hardware instalados.", "Abrir", () => OpenTargetAsync("devmgmt.msc")));
        grid.Children.Add(BuildToolActionCard("Editor do Registro", "Abre o Regedit do Windows.", "Abrir", () => OpenTargetAsync("regedit.exe")));
        grid.Children.Add(BuildToolActionCard("AMD Adrenalin", "Abre o painel Radeon quando instalado.", "Abrir", OpenAmdSoftwareAsync));
        grid.Children.Add(BuildToolActionCard("Conexões de rede", "Abre adaptadores Ethernet e Wi-Fi.", "Abrir", () => OpenTargetAsync("ncpa.cpl")));
        grid.Children.Add(BuildToolActionCard("Opções de energia", "Abre os planos e configurações de energia.", "Abrir", () => OpenTargetAsync("powercfg.cpl")));
        grid.Children.Add(BuildToolActionCard("Configurações gráficas", "GPU por aplicativo e HAGS no Windows.", "Abrir", () => OpenTargetAsync("ms-settings:display-advancedgraphics")));
        root.Children.Add(grid);
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
