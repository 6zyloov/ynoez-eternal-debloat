using System.Globalization;
using System.Management;
using System.Text;

namespace ynoez.Services;

public sealed record ServiceSafetyClassification(
    string Category,
    bool IsProtected,
    bool IsOptional,
    string Explanation);

public sealed record WindowsServiceInfo(
    string Name,
    string DisplayName,
    string Description,
    string State,
    string StartMode,
    uint ProcessId,
    ServiceSafetyClassification Safety)
{
    public string StateDisplay => State switch
    {
        "Running" => "Em execução",
        "Stopped" => "Parado",
        "Start Pending" => "Iniciando",
        "Stop Pending" => "Parando",
        "Continue Pending" => "Retomando",
        "Pause Pending" => "Pausando",
        "Paused" => "Pausado",
        _ => string.IsNullOrWhiteSpace(State) ? "Desconhecido" : State
    };

    public string StartModeDisplay => StartMode switch
    {
        "Auto" => "Automático",
        "Manual" => "Manual",
        "Disabled" => "Desativado",
        "Boot" => "Boot",
        "System" => "Sistema",
        _ => string.IsNullOrWhiteSpace(StartMode) ? "Desconhecido" : StartMode
    };

    public string ProcessIdDisplay => ProcessId == 0 ? "—" : ProcessId.ToString(CultureInfo.InvariantCulture);
    public string SafetyDisplay => Safety.Category;
}

public static class WindowsServiceInventoryService
{
    private static readonly IReadOnlyDictionary<string, string> ProtectedServices =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["WSearch"] = "Windows Search e pesquisa do menu Iniciar",
            ["Spooler"] = "impressão e fila de impressoras",
            ["WinDefend"] = "Microsoft Defender",
            ["WdNisSvc"] = "proteção de rede do Defender",
            ["Sense"] = "Microsoft Defender for Endpoint",
            ["SecurityHealthService"] = "Segurança do Windows",
            ["wuauserv"] = "Windows Update",
            ["UsoSvc"] = "orquestração do Windows Update",
            ["WaaSMedicSvc"] = "recuperação do Windows Update",
            ["BITS"] = "transferências em segundo plano usadas pelo Windows",
            ["DoSvc"] = "otimização de entrega do Windows",
            ["AppXSvc"] = "implantação de aplicativos Windows/Store",
            ["ClipSVC"] = "licenciamento da Microsoft Store",
            ["InstallService"] = "instalação da Microsoft Store",
            ["StateRepository"] = "banco de dados de estado de aplicativos Windows",
            ["mpssvc"] = "Firewall do Windows",
            ["BFE"] = "Base Filtering Engine, infraestrutura do Firewall",
            ["Dhcp"] = "conectividade DHCP",
            ["Dnscache"] = "resolução DNS",
            ["NlaSvc"] = "identificação de rede",
            ["WlanSvc"] = "Wi-Fi do Windows",
            ["Netman"] = "conexões de rede",
            ["Nsi"] = "interface de armazenamento de rede",
            ["AudioSrv"] = "áudio do Windows",
            ["AudioEndpointBuilder"] = "dispositivos e endpoints de áudio",
            ["RpcSs"] = "chamada de procedimento remoto",
            ["DcomLaunch"] = "inicialização de processos COM/DCOM",
            ["EventLog"] = "log de eventos do Windows",
            ["EventSystem"] = "notificação de eventos do sistema",
            ["Schedule"] = "Agendador de Tarefas",
            ["Power"] = "gerenciamento de energia",
            ["Appinfo"] = "elevação/UAC de aplicativos",
            ["CryptSvc"] = "serviços criptográficos do Windows",
            ["TrustedInstaller"] = "instalação e manutenção de componentes do Windows",
            ["Winmgmt"] = "Windows Management Instrumentation (WMI)",
            ["TBS"] = "serviços de TPM/Trusted Platform Module",
            ["vgc"] = "Riot Vanguard",
            ["vgk"] = "driver de kernel do Riot Vanguard",
            ["FaceitService"] = "FACEIT Anti-Cheat",
            ["FACEITService"] = "FACEIT Anti-Cheat",
            ["EasyAntiCheat"] = "Easy Anti-Cheat",
            ["EasyAntiCheat_EOS"] = "Easy Anti-Cheat EOS",
            ["BEService"] = "BattlEye Anti-Cheat",
            ["NVDisplay.ContainerLocalSystem"] = "componentes NVIDIA e painel do driver",
            ["NvContainerLocalSystem"] = "componentes de contêiner NVIDIA",
            ["NvContainerNetworkService"] = "serviços de rede de contêiner NVIDIA"
        };

    private static readonly HashSet<string> OptionalServices =
        ServiceTweakService.GetCatalog()
            .Select(x => x.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private const string Query =
        "SELECT Name, DisplayName, Description, State, StartMode, ProcessId FROM Win32_Service";

    public static ServiceSafetyClassification ClassifyService(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentException("Nome do serviço vazio.", nameof(serviceName));

        if (ProtectedServices.TryGetValue(serviceName.Trim(), out var reason))
            return new ServiceSafetyClassification(
                "Protegido pelo ynoez", true, false,
                "Preservado: " + reason + ". O inventário não oferece alterações.");

        if (OptionalServices.Contains(serviceName.Trim()))
            return new ServiceSafetyClassification(
                "Opcional avaliado", false, true,
                "Este serviço consta na whitelist restrita de Debloat. Qualquer alteração fica na página Debloat, com confirmação, backup, verificação e restauração.");

        return new ServiceSafetyClassification(
            "Somente leitura", false, false,
            "Não existe uma regra de alteração aprovada para este serviço. A Toolbox apenas consulta o estado e abre o console oficial do Windows.");
    }

    public static async Task<IReadOnlyList<WindowsServiceInfo>> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => Capture(cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    internal static IReadOnlyList<WindowsServiceInfo> Capture(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = new ManagementScope(@"\\.\root\CIMV2");
        var options = new System.Management.EnumerationOptions
        {
            ReturnImmediately = false,
            Rewindable = false,
            Timeout = TimeSpan.FromSeconds(20)
        };

        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(Query), options);
        using var results = searcher.Get();
        var services = new List<WindowsServiceInfo>();

        foreach (ManagementObject item in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (item)
            {
                var name = ReadString(item, "Name");
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                services.Add(new WindowsServiceInfo(
                    name,
                    ReadString(item, "DisplayName"),
                    ReadString(item, "Description"),
                    ReadString(item, "State"),
                    ReadString(item, "StartMode"),
                    ReadUInt32(item, "ProcessId"),
                    ClassifyService(name)));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return services
            .OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string BuildCsv(IEnumerable<WindowsServiceInfo> services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var rows = new List<string>
        {
            string.Join(",", new[]
            {
                EscapeCsv("ServiceName"),
                EscapeCsv("DisplayName"),
                EscapeCsv("State"),
                EscapeCsv("StartMode"),
                EscapeCsv("ProcessId"),
                EscapeCsv("SafetyCategory"),
                EscapeCsv("SafetyExplanation"),
                EscapeCsv("Description")
            })
        };

        foreach (var service in services)
        {
            rows.Add(string.Join(",", new[]
            {
                EscapeCsv(service.Name),
                EscapeCsv(service.DisplayName),
                EscapeCsv(service.StateDisplay),
                EscapeCsv(service.StartModeDisplay),
                EscapeCsv(service.ProcessIdDisplay),
                EscapeCsv(service.Safety.Category),
                EscapeCsv(service.Safety.Explanation),
                EscapeCsv(service.Description)
            }));
        }

        return string.Join(Environment.NewLine, rows) + Environment.NewLine;
    }

    public static string EscapeCsv(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static string ReadString(ManagementObject item, string property) =>
        Convert.ToString(item[property], CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;

    private static uint ReadUInt32(ManagementObject item, string property)
    {
        try
        {
            return Convert.ToUInt32(item[property], CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return 0;
        }
    }
}
