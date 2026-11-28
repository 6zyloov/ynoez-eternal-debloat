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

    public bool CanRestore(string name)
    {
        Validate(name);
        return File.Exists(GetBackupPath(name));
    }

    public IReadOnlyList<OptionalServiceDefinition> GetPendingRestoreDefinitions() =>
        Catalog.Where(x => CanRestore(x.Name)).ToArray();

    public IReadOnlyList<OptionalServiceStatus> GetStatuses()
    {
        var result = new List<OptionalServiceStatus>();
        foreach (var definition in Catalog)
        {
            var hasBackup = CanRestore(definition.Name);
            OptionalServiceBackup? backup = null;
            if (hasBackup)
            {
                try { backup = ReadBackup(definition.Name); }
                catch (Exception ex) { AppLog.Write("Backup de serviço inválido (" + definition.Name + "): " + ex.Message); }
            }

            var info = ReadService(definition.Name);
            if (info is null)
            {
                // Keep orphaned recovery files visible even when the service is gone or WMI cannot read it.
                if (hasBackup)
                    result.Add(new OptionalServiceStatus(definition, false, "Não encontrado ou consulta indisponível",
                        "Indisponível", false, true, backup?.WasRunning ?? false));
                continue;
            }

            result.Add(new OptionalServiceStatus(definition, true, info.Value.State, info.Value.StartMode,
                info.Value.Delayed, hasBackup, backup?.WasRunning ?? false));
        }
        return result;
    }

    public static bool IsRestorableStartMode(string? mode) =>
        string.Equals(mode, "Auto", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mode, "Manual", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<string> DisableMany(IEnumerable<string> serviceNames)
    {
        ArgumentNullException.ThrowIfNull(serviceNames);
        var requested = serviceNames.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var definitions = Catalog.Where(x => requested.Contains(x.Name, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (definitions.Length != requested.Length)
            throw new InvalidOperationException("A lista contém serviço fora da allowlist de debloat.");

        var pending = new List<(OptionalServiceDefinition Definition, OptionalServiceBackup Backup, string Path)>();
        foreach (var definition in definitions)
        {
            Validate(definition.Name);
            var info = ReadService(definition.Name);
            if (info is null || string.Equals(info.Value.StartMode, "Disabled", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!IsRestorableStartMode(info.Value.StartMode))
                throw new InvalidOperationException("O serviço " + definition.Name + " possui modo original não restaurável.");
            var backupPath = GetBackupPath(definition.Name);
            if (File.Exists(backupPath))
                throw new InvalidOperationException("O serviço " + definition.Name + " já possui backup pendente.");
            var backup = new OptionalServiceBackup(definition.Name, info.Value.StartMode,
                info.Value.Delayed, string.Equals(info.Value.State, "Running", StringComparison.OrdinalIgnoreCase),
                DateTimeOffset.UtcNow);
            WriteAtomic(backupPath, JsonSerializer.Serialize(backup, JsonOptions));
            pending.Add((definition, backup, backupPath));
        }

        if (pending.Count == 0) return Array.Empty<string>();

        var commands = new List<string> { "$ErrorActionPreference='Stop'" };
        foreach (var item in pending)
        {
            var name = item.Definition.Name;
            commands.Add("$n='" + name + "'; $s=Get-CimInstance Win32_Service -Filter \"Name='$n'\" -ErrorAction Stop");
            commands.Add("if ($null -ne $s -and $s.StartMode -ne 'Disabled') { Set-Service -Name $n -StartupType Disabled; if ((Get-Service -Name $n).Status -eq 'Running') { Stop-Service -Name $n -ErrorAction Stop; (Get-Service -Name $n).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(15)) }; $v=Get-CimInstance Win32_Service -Filter \"Name='$n'\" -ErrorAction Stop; if ($v.StartMode -ne 'Disabled' -or $v.State -eq 'Running') { throw 'Não foi possível desativar ' + $n } }");
        }
        commands.Add("exit 0");

        try
        {
            RunElevatedPowerShell(string.Join("; ", commands));
            foreach (var item in pending)
            {
                var after = ReadService(item.Definition.Name);
                if (after is null ||
                    !string.Equals(after.Value.StartMode, "Disabled", StringComparison.OrdinalIgnoreCase) ||
                    (item.Backup.WasRunning && string.Equals(after.Value.State, "Running", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("O Windows não confirmou a desativação de " + item.Definition.Name + ".");
            }

            AppLog.Write("Debloat em lote: serviços desativados " +
                string.Join(", ", pending.Select(x => x.Definition.Name)) + ".");
            return pending.Select(x => x.Definition.Name).ToArray();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            foreach (var item in pending)
                try { if (File.Exists(item.Path)) File.Delete(item.Path); } catch { }
            throw new OperationCanceledException("A elevação foi cancelada. Nenhum serviço foi alterado.", ex);
        }
        catch (Exception applyError)
        {
            try
            {
                var rollback = new List<string> { "$ErrorActionPreference='Stop'" };
                foreach (var item in pending)
                {
                    var name = item.Definition.Name;
                    rollback.Add("$n='" + name + "'; " +
                        BuildRestoreModeScript(name, item.Backup.StartMode, item.Backup.DelayedAutoStart) +
                        (item.Backup.WasRunning
                            ? "if ((Get-Service -Name $n).Status -ne 'Running') { Start-Service -Name $n; (Get-Service -Name $n).WaitForStatus('Running',[TimeSpan]::FromSeconds(15)) }; "
                            : ""));
                }
                rollback.Add("exit 0");
                RunElevatedPowerShell(string.Join("; ", rollback));
                foreach (var item in pending)
                {
                    var after = ReadService(item.Definition.Name);
                    if (after is null ||
                        !string.Equals(after.Value.StartMode, item.Backup.StartMode, StringComparison.OrdinalIgnoreCase) ||
                        after.Value.Delayed != item.Backup.DelayedAutoStart ||
                        (item.Backup.WasRunning && !string.Equals(after.Value.State, "Running", StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException("A reversão não foi confirmada para " + item.Definition.Name + ".");
                }
                foreach (var item in pending)
                    if (File.Exists(item.Path)) File.Delete(item.Path);
            }
            catch (Exception restoreError)
            {
                AppLog.Write("Debloat de serviços falhou e os backups foram mantidos: " + restoreError.Message);
                throw new InvalidOperationException(
                    "A desativação em lote falhou e a reversão não pôde ser confirmada. Os backups foram preservados. " +
                    applyError.Message + " | " + restoreError.Message, applyError);
            }
            throw;
        }
    }

    public void Disable(OptionalServiceDefinition definition)
    {
        Validate(definition.Name);
        var info = ReadService(definition.Name)
            ?? throw new InvalidOperationException("O serviço não está instalado nesta edição do Windows.");
        if (string.Equals(info.StartMode, "Disabled", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("O serviço já está desativado.");
        if (!IsRestorableStartMode(info.StartMode))
            throw new InvalidOperationException("O modo original '" + info.StartMode +
                "' não tem restauração suportada. Nenhuma alteração foi aplicada.");
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
                ? "Set-Service -Name $n -StartupType Manual; if ((Get-Service -Name $n).Status -ne 'Running') { Start-Service -Name $n; (Get-Service -Name $n).WaitForStatus('Running',[TimeSpan]::FromSeconds(12)) }; Set-Service -Name $n -StartupType Disabled; "
                : "if ((Get-Service -Name $n).Status -ne 'Running') { Start-Service -Name $n; (Get-Service -Name $n).WaitForStatus('Running',[TimeSpan]::FromSeconds(12)) }; ")
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
            "Auto" when delayed => "$sc='" + AppPaths.ScExecutable.Replace("'", "''", StringComparison.Ordinal) + "'; & $sc config $n start= delayed-auto | Out-Null; if ($LASTEXITCODE -ne 0) { throw 'sc.exe falhou' }; ",
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
            StartInfo = new ProcessStartInfo(AppPaths.PowerShellExecutable,
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
