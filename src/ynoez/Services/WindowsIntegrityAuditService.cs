using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ynoez.Services;

public sealed record WindowsIntegrityServiceStatus(
    string Name, bool Installed, string State, string StartMode, bool KernelDriver);

public sealed record WindowsIntegritySnapshot(
    DateTimeOffset CapturedAt,
    bool? DefenderRealtime,
    bool? DefenderTamperProtection,
    bool? FirewallEnabled,
    bool? TpmPresent,
    bool? TpmReady,
    bool? SecureBootEnabled,
    int? VbsStatus,
    bool? HvciConfigured,
    bool? HvciRunning,
    IReadOnlyList<WindowsIntegrityServiceStatus> Services);

public sealed record WindowsIntegrityAuditRow(string Area, string Status, string Details);

public static class WindowsIntegrityAuditService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const string Script = @"
$ErrorActionPreference = 'Stop'
$defenderRealtime = $null
$defenderTamper = $null
try {
    $d = Get-MpComputerStatus -ErrorAction Stop
    if ($null -ne $d.RealTimeProtectionEnabled) { $defenderRealtime = [bool]$d.RealTimeProtectionEnabled }
    if ($d.PSObject.Properties.Name -contains 'IsTamperProtected') { $defenderTamper = [bool]$d.IsTamperProtected }
} catch {}

$firewallEnabled = $null
try {
    $profiles = @(Get-NetFirewallProfile -PolicyStore ActiveStore -ErrorAction Stop)
    if ($profiles.Count -gt 0) {
        $firewallEnabled = (@($profiles | Where-Object { -not $_.Enabled }).Count -eq 0)
    }
} catch {}

$tpmPresent = $null
$tpmReady = $null
try {
    $t = Get-Tpm -ErrorAction Stop
    $tpmPresent = [bool]$t.TpmPresent
    $tpmReady = [bool]$t.TpmReady
} catch {}

$secureBoot = $null
try { $secureBoot = [bool](Confirm-SecureBootUEFI -ErrorAction Stop) } catch {}

$vbsStatus = $null
$hvciConfigured = $null
$hvciRunning = $null
try {
    $g = Get-CimInstance -Namespace root\Microsoft\Windows\DeviceGuard -ClassName Win32_DeviceGuard -ErrorAction Stop
    $vbsStatus = [int]$g.VirtualizationBasedSecurityStatus
    $hvciConfigured = (@($g.SecurityServicesConfigured) -contains 2)
    $hvciRunning = (@($g.SecurityServicesRunning) -contains 2)
} catch {}

$names = @('WinDefend','wuauserv','WSearch','Spooler','ClipSVC','InstallService','vgc','vgk','FACEITService','faceit')
$services = @(foreach ($name in $names) {
    $filter = 'Name=' + [char]39 + $name + [char]39
    $s = Get-CimInstance -ClassName Win32_Service -Filter $filter -ErrorAction SilentlyContinue
    if ($null -ne $s) {
        [pscustomobject]@{ Name=$name; Installed=$true; State=[string]$s.State; StartMode=[string]$s.StartMode; KernelDriver=$false }
    } else {
        $driver = Get-CimInstance -ClassName Win32_SystemDriver -Filter $filter -ErrorAction SilentlyContinue
        if ($null -ne $driver) {
            [pscustomobject]@{ Name=$name; Installed=$true; State=[string]$driver.State; StartMode=[string]$driver.StartMode; KernelDriver=$true }
        } else {
            [pscustomobject]@{ Name=$name; Installed=$false; State='Absent'; StartMode='Unknown'; KernelDriver=$false }
        }
    }
})

$result = [pscustomobject]@{
    CapturedAt = [DateTimeOffset]::UtcNow.ToString('o')
    DefenderRealtime = $defenderRealtime
    DefenderTamperProtection = $defenderTamper
    FirewallEnabled = $firewallEnabled
    TpmPresent = $tpmPresent
    TpmReady = $tpmReady
    SecureBootEnabled = $secureBoot
    VbsStatus = $vbsStatus
    HvciConfigured = $hvciConfigured
    HvciRunning = $hvciRunning
    Services = $services
}
ConvertTo-Json -InputObject $result -Depth 6 -Compress
";

    public static async Task<WindowsIntegritySnapshot> CaptureAsync(CancellationToken token = default)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(Script));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(AppPaths.PowerShellExecutable,
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            }
        };

        if (!process.Start())
            throw new InvalidOperationException("Não foi possível iniciar a consulta de integridade do Windows.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            var output = await outputTask;
            var error = await errorTask;
            token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0)
                throw new InvalidOperationException("A consulta de integridade falhou (código " +
                    process.ExitCode + "): " + error.Trim());
            return ParseJson(output);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            if (token.IsCancellationRequested) throw;
            throw new TimeoutException("A auditoria de integridade excedeu 25 segundos.");
        }
    }

    internal static WindowsIntegritySnapshot ParseJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("A consulta de integridade não retornou JSON.");
        var snapshot = JsonSerializer.Deserialize<WindowsIntegritySnapshot>(json.Trim().TrimStart('\uFEFF'), JsonOptions)
            ?? throw new InvalidDataException("O relatório de integridade está vazio.");
        if (snapshot.CapturedAt == default || snapshot.Services is null)
            throw new InvalidDataException("O relatório de integridade não contém data ou lista de serviços.");
        if (snapshot.Services.Any(x => x is null || string.IsNullOrWhiteSpace(x.Name)) ||
            snapshot.Services.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != snapshot.Services.Count)
            throw new InvalidDataException("O relatório contém serviços sem nome ou duplicados.");
        if (snapshot.VbsStatus is < 0 or > 2)
            throw new InvalidDataException("O estado VBS retornado pelo Windows não é reconhecido.");
        return snapshot;
    }

    public static IReadOnlyList<WindowsIntegrityAuditRow> BuildRows(WindowsIntegritySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var rows = new List<WindowsIntegrityAuditRow>
        {
            BoolRow("Microsoft Defender — proteção em tempo real", snapshot.DefenderRealtime,
                "Indica se a proteção em tempo real reportada pelo módulo Defender está ativa."),
            BoolRow("Microsoft Defender — proteção contra adulteração", snapshot.DefenderTamperProtection,
                "Estado de Tamper Protection quando fornecido pela versão instalada do Defender."),
            BoolRow("Firewall do Windows (todos os perfis)", snapshot.FirewallEnabled,
                "Considera ativo somente quando todos os perfis consultados estão habilitados."),
            BoolRow("TPM presente", snapshot.TpmPresent, "Presença do TPM informada pelo cmdlet Get-Tpm."),
            BoolRow("TPM pronto", snapshot.TpmReady, "Estado de prontidão reportado pelo Windows."),
            BoolRow("Secure Boot", snapshot.SecureBootEnabled, "Confirm-SecureBootUEFI; em BIOS legado ou firmware não consultável fica indisponível."),
            VbsRow(snapshot.VbsStatus),
            BoolRow("HVCI configurado", snapshot.HvciConfigured, "Serviços de segurança configurados no Win32_DeviceGuard."),
            BoolRow("HVCI em execução", snapshot.HvciRunning, "Serviços de segurança em execução no Win32_DeviceGuard.")
        };

        AddServiceRow(rows, snapshot, "WinDefend", "Serviço Defender");
        AddServiceRow(rows, snapshot, "wuauserv", "Windows Update");
        AddServiceRow(rows, snapshot, "WSearch", "Windows Search");
        AddServiceRow(rows, snapshot, "Spooler", "Impressão");
        AddServiceRow(rows, snapshot, "ClipSVC", "Microsoft Store — licenciamento");
        AddServiceRow(rows, snapshot, "InstallService", "Microsoft Store — instalação");
        AddServiceRow(rows, snapshot, "vgc", "Riot Vanguard — serviço");
        AddServiceRow(rows, snapshot, "vgk", "Riot Vanguard — driver");
        AddServiceRow(rows, snapshot, "FACEITService", "FACEIT Anti-Cheat");
        AddServiceRow(rows, snapshot, "faceit", "FACEIT Anti-Cheat (driver/serviço)");
        return rows;
    }

    private static WindowsIntegrityAuditRow BoolRow(string area, bool? value, string detail) =>
        new(area, value.HasValue ? (value.Value ? "Ativado" : "Desativado") : "Indisponível", detail);

    private static WindowsIntegrityAuditRow VbsRow(int? status) => status switch
    {
        0 => new WindowsIntegrityAuditRow("VBS", "Desativado", "Virtualization Based Security não está habilitado."),
        1 => new WindowsIntegrityAuditRow("VBS", "Configurado, não ativo", "VBS está configurado, mas não foi reportado como em execução."),
        2 => new WindowsIntegrityAuditRow("VBS", "Em execução", "Virtualization Based Security está em execução."),
        _ => new WindowsIntegrityAuditRow("VBS", "Indisponível", "O Windows não disponibilizou Win32_DeviceGuard.")
    };

    private static void AddServiceRow(List<WindowsIntegrityAuditRow> rows,
        WindowsIntegritySnapshot snapshot, string name, string label)
    {
        var service = snapshot.Services.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (service is null || !service.Installed)
        {
            rows.Add(new WindowsIntegrityAuditRow(label, "Não instalado", "Nenhum serviço/driver com este nome foi encontrado."));
            return;
        }

        var status = string.Equals(service.State, "Running", StringComparison.OrdinalIgnoreCase)
            ? "Em execução"
            : string.Equals(service.State, "Stopped", StringComparison.OrdinalIgnoreCase)
                ? "Parado"
                : service.State;
        var kind = service.KernelDriver ? "Driver de kernel" : "Serviço";
        rows.Add(new WindowsIntegrityAuditRow(label, status,
            kind + " • inicialização: " + service.StartMode));
    }
}
