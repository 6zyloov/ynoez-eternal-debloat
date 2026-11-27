using System.ComponentModel;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ynoez.Services;

public sealed record OptionalServiceDefinition(string Name, string DisplayName, string Description, string Risk);
public sealed record OptionalServiceStatus(
    OptionalServiceDefinition Definition, bool Available, string State, string StartMode,
    bool DelayedAutoStart, bool DisabledByYnoez, bool WasRunningBeforeDisable);

public sealed record OptionalServiceBackup(
    string Name, string StartMode, bool DelayedAutoStart, bool WasRunning, DateTimeOffset CapturedAt);

public sealed class ServiceTweakService
{
    private static readonly OptionalServiceDefinition[] Catalog =
    {
        new("DiagTrack", "Experiências do usuário conectado e telemetria", "Reduz o serviço de telemetria e diagnóstico conectado do Windows. Pode limitar dados de diagnóstico enviados à Microsoft.", "Médio"),
        new("dmwappushservice", "WAP Push / gerenciamento de dispositivos", "Desativa o serviço WAP Push usado por alguns cenários de gerenciamento e diagnóstico. Não afeta Windows Update.", "Médio"),
        new("MapsBroker", "Gerenciador de mapas baixados", "Desativa o serviço de mapas offline. A navegação e mapas baixados podem deixar de atualizar em segundo plano.", "Baixo"),
        new("Fax", "Fax", "Desativa o serviço de fax do Windows. Não afeta impressão comum.", "Baixo"),
        new("RetailDemo", "Demonstração de varejo", "Desativa o serviço de demonstração de dispositivos de exposição em lojas.", "Baixo"),
        new("WMPNetworkSvc", "Compartilhamento de mídia do Windows Media Player", "Desativa o compartilhamento de bibliotecas do Windows Media Player pela rede.", "Baixo")
    };
    private static readonly HashSet<string> Allowed = Catalog.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
    private static readonly string[] Protected =
    {
        "WSearch", "Spooler", "WinDefend", "wuauserv", "UsoSvc", "BITS", "DoSvc", "AppXSvc",
        "ClipSVC", "SecurityHealthService", "vgc", "FaceitService", "NlaSvc", "Dhcp", "Dnscache",
        "WlanSvc", "AudioSrv", "RpcSs", "DcomLaunch", "EventLog", "Schedule", "Power"
    };
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _backupDirectory;

    public ServiceTweakService(string? backupDirectory = null)
    {
        _backupDirectory = backupDirectory ?? Path.Combine(AppPaths.Data, "ServiceRestore");
        Directory.CreateDirectory(_backupDirectory);
    }

    public static IReadOnlyList<OptionalServiceDefinition> GetCatalog() => Catalog;

    public IReadOnlyList<OptionalServiceStatus> GetStatuses()
    {
        var result = new List<OptionalServiceStatus>();
        foreach (var definition in Catalog)
        {
            var info = ReadService(definition.Name);
            if (info is null) continue;
            OptionalServiceBackup? backup = null;
            var hasBackup = File.Exists(GetBackupPath(definition.Name));
            try { backup = ReadBackup(definition.Name); }
            catch (Exception ex) { AppLog.Write("Backup de serviço inválido (" + definition.Name + "): " + ex.Message); }
            result.Add(new OptionalServiceStatus(definition, true, info.State, info.StartMode,
                info.Delayed, hasBackup, backup?.WasRunning ?? false));
        }
        return result;
    }

    public void Disable(OptionalServiceDefinition definition)
    {
        Validate(definition.Name);
        var info = ReadService(definition.Name)
            ?? throw new InvalidOperationException("O serviço não está instalado nesta edição do Windows.");
        if (string.Equals(info.StartMode, "Disabled", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("O serviço já está desativado.");
        var backupPath = GetBackupPath(definition.Name);
        if (File.Exists(backupPath))
            throw new InvalidOperationException("Já existe um backup deste serviço. Restaure-o antes de aplicar novamente.");

        var backup = new OptionalServiceBackup(definition.Name, info.StartMode, info.Delayed,
            string.Equals(info.State, "Running", StringComparison.OrdinalIgnoreCase), DateTimeOffset.UtcNow);
        WriteAtomic(backupPath, JsonSerializer.Serialize(backup, JsonOptions));

        var restoreMode = BuildRestoreModeScript(definition.Name, backup.StartMode, backup.DelayedAutoStart);
        var script = "$ErrorActionPreference='Stop'; $n='" + definition.Name + "'; try { " +
            "Set-Service -Name $n -StartupType Disabled; " +
            (backup.WasRunning ? "Stop-Service -Name $n -ErrorAction Stop; (Get-Service -Name $n).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(12)); " : "") +
            "} catch { try { " + restoreMode + " } catch {}; exit 2 }; exit 0";

        try
        {
            RunElevatedPowerShell(script);
            var after = ReadService(definition.Name);
            if (after is null || !string.Equals(after.Value.StartMode, "Disabled", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("O Windows não confirmou a desativação do serviço.");
            if (backup.WasRunning && string.Equals(after.Value.State, "Running", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("O serviço continuou em execução; o backup foi mantido para recuperação.");
            AppLog.Write("Serviço opcional desativado: " + definition.Name);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            try { File.Delete(backupPath); } catch { }
            throw new OperationCanceledException("A elevação foi cancelada. Nenhuma alteração foi aplicada.", ex);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            try { File.Delete(backupPath); } catch { }
            throw new OperationCanceledException("A elevação foi cancelada. Nenhuma alteração foi aplicada.", ex);
        }
        catch
        {
            // Keep the backup if any part of the operation is uncertain.
            throw;
        }
    }

    public void Restore(OptionalServiceDefinition definition)
    {
        Validate(definition.Name);
        var backup = ReadBackup(definition.Name)
            ?? throw new InvalidOperationException("Não existe backup para este serviço.");
        var modeScript = BuildRestoreModeScript(definition.Name, backup.StartMode, backup.DelayedAutoStart);
        var startScript = backup.WasRunning
            ? (backup.StartMode == "Disabled"
                ? "Set-Service -Name $n -StartupType Manual; Start-Service -Name $n; (Get-Service -Name $n).WaitForStatus('Running',[TimeSpan]::FromSeconds(12)); Set-Service -Name $n -StartupType Disabled; "
                : "Start-Service -Name $n; (Get-Service -Name $n).WaitForStatus('Running',[TimeSpan]::FromSeconds(12)); ")
            : "";
        var script = "$ErrorActionPreference='Stop'; $n='" + definition.Name + "'; " + modeScript + startScript + "exit 0";
        RunElevatedPowerShell(script);

        var after = ReadService(definition.Name);
        if (after is null || !string.Equals(after.Value.StartMode, backup.StartMode, StringComparison.OrdinalIgnoreCase)
            || after.Value.Delayed != backup.DelayedAutoStart
            || (backup.WasRunning && !string.Equals(after.Value.State, "Running", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A verificação da restauração do serviço falhou; o backup foi mantido.");

        File.Delete(GetBackupPath(definition.Name));
        AppLog.Write("Serviço opcional restaurado: " + definition.Name);
    }

    private OptionalServiceBackup? ReadBackup(string name)
    {
        var path = GetBackupPath(name);
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<OptionalServiceBackup>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("Backup de serviço inválido: " + name);
    }

    private string GetBackupPath(string name)
    {
        Validate(name);
        return Path.Combine(_backupDirectory, name + ".json");
    }

    private static void Validate(string name)
    {
        if (!Allowed.Contains(name) || Protected.Contains(name, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Este serviço não pertence à lista de serviços opcionais permitidos.");
    }

    private static (string State, string StartMode, bool Delayed)? ReadService(string name)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\CIMV2",
                "SELECT State, StartMode FROM Win32_Service WHERE Name='" + name + "'");
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    var state = Convert.ToString(item["State"]) ?? "Unknown";
                    var mode = Convert.ToString(item["StartMode"]) ?? "Unknown";
                    var delayed = false;
                    using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name);
                    delayed = Convert.ToInt32(key?.GetValue("DelayedAutoStart", 0) ?? 0) == 1;
                    return (state, mode, delayed);
                }
            }
        }
        catch (Exception ex) { AppLog.Write("Falha ao consultar serviço " + name + ": " + ex.Message); }
        return null;
    }

    private static string BuildRestoreModeScript(string name, string mode, bool delayed)
    {
        var safeMode = mode switch
        {
            "Auto" when delayed => "sc.exe config $n start= delayed-auto | Out-Null; if ($LASTEXITCODE -ne 0) { throw 'sc.exe falhou' }; ",
            "Auto" => "Set-Service -Name $n -StartupType Automatic; ",
            "Manual" => "Set-Service -Name $n -StartupType Manual; ",
            "Disabled" => "Set-Service -Name $n -StartupType Disabled; ",
            _ => throw new InvalidDataException("Modo de inicialização original desconhecido para " + name + ".")
        };
        return safeMode;
    }

    private static void RunElevatedPowerShell(string script)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var isAdmin = false;
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            isAdmin = new System.Security.Principal.WindowsPrincipal(identity)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("powershell.exe",
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded)
            {
                UseShellExecute = true,
                Verb = isAdmin ? "" : "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            }
        };
        process.Start();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException("O Windows recusou a alteração do serviço (código " + process.ExitCode + "). O backup foi preservado.");
    }

    private static void WriteAtomic(string path, string content)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, overwrite: true);
    }
}
