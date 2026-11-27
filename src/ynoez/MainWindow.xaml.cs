using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using Microsoft.Win32;
using ynoez.Services;

namespace ynoez;

public partial class MainWindow : Window
{
    private static readonly Brush PageBrush = BrushFrom("#121212");
    private static readonly Brush CardBrush = BrushFrom("#1E1E1E");
    private static readonly Brush AccentBrush = BrushFrom("#FF80FF");
    private static readonly Brush PrimaryBrush = BrushFrom("#FFFFFF");
    private static readonly Brush SecondaryBrush = BrushFrom("#AFAFAF");
    private static readonly Brush MutedBrush = BrushFrom("#787878");
    private static readonly Brush BorderBrushSoft = BrushFrom("#333333");

    private HardwareSnapshot _hardware = new();
    private string _currentPage = "Home";
    private bool _isBusy;
    private CancellationTokenSource? _networkMonitorCts;

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
        Closed += (_, _) => _networkMonitorCts?.Cancel();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshHardwareAsync();
        ShowPage("Home");
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
            ["Home"] = NavHome, ["Backup"] = NavBackup, ["Tweaks"] = NavTweaks,
            ["Network"] = NavNetwork, ["Debloat"] = NavDebloat, ["Motherboard"] = NavBoard,
            ["Toolbox"] = NavToolbox, ["Restore"] = NavRestore, ["Logs"] = NavLogs
        };
        foreach (var item in map)
        {
            item.Value.Background = item.Key == page ? BrushFrom("#262026") : PageBrush;
            item.Value.Foreground = item.Key == page ? AccentBrush : PrimaryBrush;
        }
    }

    private UIElement BuildHome()
    {
        var root = PageContainer();
        root.Children.Add(PageIntro("System Info", "Visão geral do hardware e do Windows detectados neste computador."));

        var grid = new UniformGrid { Columns = 3, Rows = 2, Margin = new Thickness(0, 3, 0, 18) };
        grid.Children.Add(InfoTile("CPU", _hardware.Cpu, "◉"));
        grid.Children.Add(InfoTile("RAM", _hardware.Memory, "▤"));
        grid.Children.Add(InfoTile("Graphics Card", _hardware.Gpu, "▧"));
        grid.Children.Add(InfoTile("Network", _hardware.NetworkSummary, "⌁"));
        grid.Children.Add(InfoTile("Motherboard", _hardware.BoardProduct, "▦"));
        grid.Children.Add(InfoTile("Windows Version", _hardware.OperatingSystem + "\nBuild " + _hardware.WindowsBuild, "▣"));
        root.Children.Add(grid);

        var info = MakeCard();
        var panel = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
        panel.Children.Add(Heading("Information", 17));
        panel.Children.Add(Body("O ynoez funciona localmente. Nenhum ajuste é aplicado em segundo plano. Cada alteração disponível fica identificada, exige ação explícita e registra o que foi feito."));
        var row = new WrapPanel { Margin = new Thickness(0, 13, 0, 0) };
        row.Children.Add(Badge("BACKUP ANTES DE AJUSTAR", "#FF80FF"));
        row.Children.Add(Badge("RESTAURAÇÃO", "#FF80FF"));
        row.Children.Add(Badge("VANGUARD / FACEIT: PROTEÇÕES PRESERVADAS", "#AFAFAF"));
        panel.Children.Add(row);
        info.Child = panel;
        root.Children.Add(info);

        var actions = new WrapPanel { Margin = new Thickness(0, 15, 0, 0) };
        actions.Children.Add(SmallAction("Abrir Toolbox", () => ShowPage("Toolbox")));
        actions.Children.Add(SmallAction("Diagnosticar Rede", () => ShowPage("Network")));
        actions.Children.Add(SmallAction("Central A520M K V2", () => ShowPage("Motherboard")));
        root.Children.Add(actions);
        return root;
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
        root.Children.Add(PageIntro("Tweaks", "Ajustes de acesso controlado. Nada é aplicado automaticamente ao abrir o aplicativo."));
        root.Children.Add(ActionCard("Plano de energia Alto desempenho", "Salva o GUID do plano atual e só então solicita ao Windows o plano Alto desempenho. O resultado pode aumentar consumo e temperatura.", "Ativar Alto desempenho", async () =>
        {
            if (MessageBox.Show("Ativar o plano Alto desempenho? O plano atual será salvo e poderá ser restaurado pelo ynoez.", "Confirmar ajuste", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            var message = BackupService.SetHighPerformancePlan();
            MessageBox.Show(message, "ynoez", MessageBoxButton.OK, MessageBoxImage.Information);
        }, "REVERSÍVEL"));
        root.Children.Add(ActionCard("Restaurar plano de energia anterior", "Reaplica o GUID salvo antes da última alteração de plano feita pelo ynoez.", "Restaurar plano", () =>
        {
            var message = BackupService.RestorePreviousPowerScheme();
            MessageBox.Show(message, "ynoez", MessageBoxButton.OK, MessageBoxImage.Information);
            return Task.CompletedTask;
        }, "RESTAURAÇÃO"));
        root.Children.Add(ActionCard("Modo de Jogo do Windows", "Abre a configuração oficial. O ynoez não remove Xbox, Game Bar, capturas ou overlays.", "Abrir configuração", () => OpenTargetAsync("ms-settings:gaming-gamemode"), "SEM ALTERAÇÃO"));
        root.Children.Add(ActionCard("Energia do adaptador de rede", "Abre o painel de energia do Windows. O comportamento de economia do adaptador será avaliado no diagnóstico, sem forçar valores desconhecidos.", "Abrir energia", () => OpenTargetAsync("ms-settings:powersleep"), "SEM ALTERAÇÃO"));
        root.Children.Add(ActionCard("Windows Update e Segurança", "Atalhos oficiais para revisar atualizações e as proteções que devem continuar disponíveis para os anti-cheats.", "Abrir Windows Update", () => OpenTargetAsync("ms-settings:windowsupdate"), "PROTEGIDO"));
        root.Children.Add(Heading("Catálogo de otimizações reversíveis", 15));
        root.Children.Add(Body("Cada ajuste mostra o estado atual. Aplicar exige confirmação e cria backup individual; restaurar recupera o estado anterior. Nenhum ajuste é executado em lote."));
        try
        {
            var catalog = OptimizationCatalogService.LoadRegistryTweaks();
            var service = new RegistryTweakService();
            foreach (var tweak in catalog)
            {
                var state = service.Inspect(tweak);
                var stateText = state switch
                {
                    TweakState.Enabled => "ATIVADO",
                    TweakState.Disabled => "DESATIVADO",
                    _ => "PADRÃO / NÃO CONFIGURADO"
                };
                root.Children.Add(ActionCard(
                    tweak.Name,
                    tweak.Description + "\nCategoria: " + tweak.Category + " | Risco: " + tweak.Risk + " | Estado: " + stateText,
                    "Aplicar",
                    () =>
                    {
                        if (MessageBox.Show("Aplicar \"" + tweak.Name + "\"?\n\nO ynoez salvará o valor original antes de alterar o Registro. Você poderá restaurá-lo depois.",
                            "Confirmar otimização", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                            return Task.CompletedTask;
                        service.Apply(tweak);
                        MessageBox.Show("Ajuste aplicado e verificado. O backup original foi preservado.", "ynoez", MessageBoxButton.OK, MessageBoxImage.Information);
                        ShowPage("Tweaks");
                        return Task.CompletedTask;
                    },
                    tweak.Risk.ToUpperInvariant()));
                if (service.CanRestore(tweak.Id))
                {
                    root.Children.Add(ActionCard(
                        "Restaurar — " + tweak.Name,
                        "Recupera o valor original salvo antes da primeira aplicação deste ajuste.",
                        "Restaurar original",
                        () =>
                        {
                            if (MessageBox.Show("Restaurar o estado original de \"" + tweak.Name + "\"?", "Confirmar restauração",
                                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                                return Task.CompletedTask;
                            service.Restore(tweak);
                            MessageBox.Show("Estado original restaurado e verificado.", "ynoez", MessageBoxButton.OK, MessageBoxImage.Information);
                            ShowPage("Tweaks");
                            return Task.CompletedTask;
                        },
                        "REVERSÃO"));
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("Falha ao carregar catálogo de otimizações: " + ex);
            root.Children.Add(NoteCard("Catálogo indisponível", "Não foi possível carregar ou validar o catálogo. Nenhuma alteração foi aplicada. Detalhe: " + ex.Message));
        }
        root.Children.Add(NoteCard("Ajustes experimentais protegidos", "Timers BCD, HPET, MMCSS, VBS/HVCI, IOMMU/DMA, TPM, Secure Boot, desativação de Defender e remoção de serviços críticos não são aplicados por este painel. Não existe um ganho universal que justifique desativá-los indiscriminadamente."));
        return root;
    }

    private UIElement BuildNetwork()
    {
        var root = PageContainer();
        root.Children.Add(PageIntro("Network", "Mede latência, jitter, perda de pacotes no gateway e conectividade externa."));
        var resultCard = MakeCard();
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(Heading("Diagnóstico de conexão", 15));
        panel.Children.Add(Body("O teste envia 8 pacotes ao gateway local e 10 para 1.1.1.1, além de medir uma consulta DNS. Nenhuma configuração de rede será modificada."));
        var output = new TextBlock
        {
            Text = "Pronto para testar.", Foreground = SecondaryBrush, FontFamily = new FontFamily("Consolas"),
            FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 15, 0, 10)
        };
        var progressText = new TextBlock { Text = "", Foreground = AccentBrush, FontSize = 10, Margin = new Thickness(0, 0, 0, 10) };
        var run = new Button { Content = "Executar diagnóstico", Style = (Style)FindResource("AccentButton"), HorizontalAlignment = HorizontalAlignment.Left };
        run.Click += async (_, _) =>
        {
            run.IsEnabled = false;
            output.Text = "Executando…";
            var progress = new Progress<string>(s => progressText.Text = s);
            try { output.Text = await NetworkDiagnosticService.RunAsync(progress); }
            catch (Exception ex) { output.Text = "Falha no diagnóstico: " + ex.Message; AppLog.Write("Diagnóstico de rede: " + ex); }
            finally { run.IsEnabled = true; progressText.Text = "Concluído"; }
        };
        panel.Children.Add(progressText);
        panel.Children.Add(output);
        panel.Children.Add(run);
        resultCard.Child = panel;
        root.Children.Add(resultCard);

        var monitorCard = MakeCard();
        var monitorPanel = new StackPanel { Margin = new Thickness(20) };
        monitorPanel.Children.Add(Heading("Monitoramento contínuo", 15));
        monitorPanel.Children.Add(Body("Registra a cada 5 segundos a latência e o estado do gateway e de 1.1.1.1. Ideal para sessões longas ou períodos AFK. O CSV fica em C:\\ynoez\\Logs; o monitor não altera a rede."));
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
            monitorStatus.Text = "Monitorando gateway e 1.1.1.1…";
            try
            {
                var logFile = await NetworkMonitorService.RunAsync(TimeSpan.FromSeconds(5), sample =>
                {
                    monitorStatus.Text = sample.Timestamp.ToString("HH:mm:ss") +
                        " | Gateway: " + sample.GatewayStatus + (sample.GatewayMilliseconds.HasValue ? " " + sample.GatewayMilliseconds + " ms" : "") +
                        " | Internet: " + sample.ExternalStatus + (sample.ExternalMilliseconds.HasValue ? " " + sample.ExternalMilliseconds + " ms" : "");
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

        root.Children.Add(NoteCard("Como interpretar", "Perda também no gateway sugere um problema dentro da rede local (Wi-Fi, cabo, adaptador ou roteador). Perda somente no destino externo pode estar na rota ou no provedor. Um teste curto não prova sozinho a causa de desconexões intermitentes."));
        return root;
    }

    private UIElement BuildDebloat()
    {
        var root = PageContainer();
        root.Children.Add(PageIntro("Debloat", "Limpeza seletiva de temporários e caches. Dados pessoais, perfis e credenciais são preservados."));
        root.Children.Add(ActionCard("Limpar temporários do usuário", "Remove arquivos em uso que puderem ser apagados da pasta Temp. Itens bloqueados são ignorados; Downloads, documentos e dados de aplicativos não são tocados.", "Limpar temporários", async () =>
        {
            if (MessageBox.Show("Limpar arquivos temporários do usuário? Arquivos bloqueados serão ignorados.", "Confirmar limpeza", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            var result = await CleanupService.ClearWindowsTempAsync();
            AppLog.Write("Temp: " + result.Summary);
            MessageBox.Show(result.Summary, "Limpeza concluída", MessageBoxButton.OK, MessageBoxImage.Information);
        }, "SELETIVO"));
        root.Children.Add(ActionCard("Limpar cache do Brave", "Limpa somente Cache, Code Cache e GPUCache dos perfis encontrados. Não apaga senhas, cookies, favoritos, extensões ou histórico. Feche o Brave antes para obter melhor resultado.", "Limpar cache Brave", async () =>
        {
            if (MessageBox.Show("Limpar apenas os caches do Brave? Senhas, cookies e perfis serão preservados.", "Confirmar limpeza", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            var result = await CleanupService.ClearBraveCacheAsync();
            MessageBox.Show(result.Summary, "Brave", MessageBoxButton.OK, MessageBoxImage.Information);
        }, "PERFIL PRESERVADO"));
        root.Children.Add(ActionCard("Limpar cache do Discord", "Limpa somente Cache, Code Cache e GPUCache no perfil local do Discord. Não apaga login, dados de conta, configurações ou servidores.", "Limpar cache Discord", async () =>
        {
            if (MessageBox.Show("Limpar apenas os caches do Discord? O perfil e os dados de conta serão preservados.", "Confirmar limpeza", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            var result = await CleanupService.ClearDiscordCacheAsync();
            MessageBox.Show(result.Summary, "Discord", MessageBoxButton.OK, MessageBoxImage.Information);
        }, "PERFIL PRESERVADO"));
        root.Children.Add(Heading("Gerenciador de inicialização", 15));
        root.Children.Add(Body("Mostra entradas de inicialização do usuário e do sistema. Nesta versão, somente entradas HKCU não protegidas podem ser desativadas; entradas de drivers, segurança, NVIDIA, áudio, rede e anti-cheats ficam protegidas."));
        var startupManager = new StartupManagerService();
        foreach (var entry in startupManager.GetActiveEntries())
        {
            var detail = entry.Command + "\nOrigem: " + entry.Scope;
            if (entry.CanDisable)
            {
                root.Children.Add(ActionCard(entry.Name, detail, "Desativar inicialização", () =>
                {
                    if (MessageBox.Show("Desativar a inicialização automática de \"" + entry.Name + "\"? O comando será salvo para restauração.",
                        "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                        return Task.CompletedTask;
                    startupManager.Disable(entry);
                    MessageBox.Show("Entrada removida da inicialização e backup criado.", "ynoez", MessageBoxButton.OK, MessageBoxImage.Information);
                    ShowPage("Debloat");
                    return Task.CompletedTask;
                }, "USUÁRIO"));
            }
            else
                root.Children.Add(NoteCard(entry.Name + " — preservado", detail + "\n" + entry.ProtectionReason));
        }

        var disabledStartup = startupManager.GetDisabledEntries();
        if (disabledStartup.Count > 0)
        {
            root.Children.Add(Heading("Entradas desativadas pelo ynoez", 15));
            foreach (var entry in disabledStartup)
            {
                root.Children.Add(ActionCard(entry.Name, entry.Command, "Restaurar inicialização", () =>
                {
                    if (MessageBox.Show("Restaurar \"" + entry.Name + "\" à inicialização?", "Confirmar restauração",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                        return Task.CompletedTask;
                    startupManager.Restore(entry);
                    MessageBox.Show("Entrada restaurada.", "ynoez", MessageBoxButton.OK, MessageBoxImage.Information);
                    ShowPage("Debloat");
                    return Task.CompletedTask;
                }, "REVERSÍVEL"));
            }
        }
        else
            root.Children.Add(NoteCard("Inicialização", "Nenhuma entrada desativada pelo ynoez."));

        root.Children.Add(ProtectedItemsCard());
        return root;
    }

    private UIElement BuildMotherboard()
    {
        var root = PageContainer();
        var profile = MotherboardProfileService.Find(_hardware);
        var revision = profile is null ? null : MotherboardProfileService.GetRevision(profile, _hardware.BoardVersion);
        var recognized = profile is not null;
        var title = recognized ? $"{profile!.Manufacturer} {profile!.Product}" : "Central da placa-mãe";
        var desc = recognized
            ? $"Perfil específico identificado. Revisão: {(revision?.Revision ?? "não identificada pelo firmware")}. Firmware e drivers só serão recomendados após confirmar a revisão física."
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
            root.Children.Add(ActionCard("Página oficial de suporte", "Drivers, BIOS e utilitários da fabricante. Quando a revisão não é detectada, a página genérica permite escolher a revisão correta manualmente.", "Abrir suporte", () => OpenTargetAsync(revision?.Support ?? "https://www.gigabyte.com/br/Motherboard/A520M-K-V2"), "GIGABYTE"));
            if (!string.IsNullOrWhiteSpace(profile.ManualUrl))
                root.Children.Add(ActionCard("Manual da placa-mãe", "Documentação oficial da série A520M K. Confirme o sufixo V2 e a revisão física antes de seguir instruções de firmware ou conectores.", "Abrir manual", () => OpenTargetAsync(profile.ManualUrl), "DOCUMENTAÇÃO"));
            if (revision is not null)
            {
                var biosCard = MakeCard();
                var biosPanel = new StackPanel { Margin = new Thickness(20) };
                biosPanel.Children.Add(Heading("Última BIOS listada pela Gigabyte", 15));
                biosPanel.Children.Add(KeyValue("Revisão", revision.Revision));
                biosPanel.Children.Add(KeyValue("Versão listada", revision.LatestBiosVersion));
                biosPanel.Children.Add(KeyValue("Data de publicação", revision.LatestBiosReleaseDate));
                foreach (var note in revision.LatestBiosNotes) biosPanel.Children.Add(Bullet(note));
                biosPanel.Children.Add(Body("Consulta verificada em " + revision.BiosCheckedAt + ". Isto não significa que a atualização seja obrigatória: compare com a BIOS instalada, CPU suportada e notas oficiais antes de decidir."));
                biosCard.Child = biosPanel;
                root.Children.Add(biosCard);
                root.Children.Add(ActionCard("Página de BIOS da revisão " + revision.Revision, "Consulta oficial apenas. A atualização de firmware continua manual, depende da revisão física e não é executada pelo ynoez.", "Abrir página de BIOS", () => OpenTargetAsync(revision.Bios), "SOMENTE CONSULTA"));
            }
            else
                root.Children.Add(NoteCard("Revisão física obrigatória", "A página do modelo oferece revisões 1.0, 1.1 e 1.2. A versão SMBIOS não foi suficiente para escolher uma delas; confira a serigrafia impressa na própria placa antes de abrir ou instalar BIOS."));

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
        root.Children.Add(NoteCard("Ações de serviço", "A Toolbox localiza e abre o Gerenciador de Serviços, mas não oferece desligamento em massa. Serviços essenciais, Store, impressão, busca, atualização e segurança precisam ser avaliados individualmente antes de qualquer mudança."));
        RenderResults();
        return root;
    }

    private UIElement BuildRestore()
    {
        var root = PageContainer();
        root.Children.Add(PageIntro("Restore", "Abra a restauração do sistema ou recupere o plano de energia salvo anteriormente."));
        root.Children.Add(ActionCard("Restauração do Sistema", "Abre o utilitário nativo do Windows para escolher um ponto de restauração disponível.", "Abrir Restauração", () => OpenTargetAsync("rstrui.exe"), "WINDOWS"));
        root.Children.Add(ActionCard("Restaurar plano de energia", "Reaplica o plano salvo antes da última alteração de Alto desempenho feita pelo ynoez.", "Restaurar plano", () =>
        {
            var message = BackupService.RestorePreviousPowerScheme();
            MessageBox.Show(message, "ynoez", MessageBoxButton.OK, MessageBoxImage.Information);
            return Task.CompletedTask;
        }, "REVERSÍVEL"));

        var backups = BackupService.ListBackups();
        var listCard = MakeCard();
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(Heading("Backups locais", 15));
        panel.Children.Add(Body("Local: " + AppPaths.Backups));
        if (backups.Count == 0) panel.Children.Add(Body("Nenhum snapshot criado ainda."));
        foreach (var path in backups.Take(10)) panel.Children.Add(Body("• " + Path.GetFileName(path) + "  —  " + Directory.GetCreationTime(path).ToString("dd/MM/yyyy HH:mm")));
        listCard.Child = panel;
        root.Children.Add(listCard);
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
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = BrushFrom("#171717"),
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

    private static StackPanel PageContainer() => new() { Orientation = Orientation.Vertical, MinWidth = 550 };

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
        header.Children.Add(Badge(status, status.Contains("PROTEG") ? "#AFAFAF" : "#FF80FF"));
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
            Background = BrushFrom("#2A242A"), CornerRadius = new CornerRadius(4),
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

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
