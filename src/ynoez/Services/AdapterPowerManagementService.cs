using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ynoez.Services;

public sealed record AdapterPowerStatus(
    string Name, string InterfaceDescription, string Status, string LinkSpeed,
    string SelectiveSuspend, string DeviceSleepOnDisconnect);

public sealed record AdapterPowerBackup(
    string Name, string SelectiveSuspend, string DeviceSleepOnDisconnect, DateTimeOffset CapturedAt);

public sealed class AdapterPowerManagementService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _backupDirectory;

    public AdapterPowerManagementService(string? backupDirectory = null)
    {
        _backupDirectory = backupDirectory ?? Path.Combine(AppPaths.Data, "NetworkAdapterPower");
        Directory.CreateDirectory(_backupDirectory);
    }

    public IReadOnlyList<AdapterPowerStatus> GetPhysicalAdapters()
    {
        var outputPath = Path.Combine(AppPaths.Data, "adapter-power-" + Guid.NewGuid().ToString("N") + ".json");
        var path = EscapePowerShell(outputPath);
        var script = "$ErrorActionPreference='Stop'; try { $items = @(Get-NetAdapter -Physical | ForEach-Object { " +
            "$a=$_; try { $p=Get-NetAdapterPowerManagement -Name $a.Name -ErrorAction Stop; " +
            "[pscustomobject]@{ Name=$a.Name; InterfaceDescription=$a.InterfaceDescription; Status=[string]$a.Status; " +
            "LinkSpeed=[string]$a.LinkSpeed; SelectiveSuspend=[string]$p.SelectiveSuspend; " +
            "DeviceSleepOnDisconnect=[string]$p.DeviceSleepOnDisconnect } } catch {} }); " +
            "ConvertTo-Json -InputObject @($items) -Depth 4 | Set-Content -LiteralPath '" + path + "' -Encoding UTF8 " +
            "} catch { exit 2 }; exit 0";
        try
        {
            RunPowerShell(script);
            if (!File.Exists(outputPath)) throw new InvalidOperationException("O Windows não retornou os dados de energia dos adaptadores.");
            using var document = JsonDocument.Parse(File.ReadAllText(outputPath));
            var root = document.RootElement;
            var items = root.ValueKind == JsonValueKind.Array
                ? root.EnumerateArray().ToArray()
                : root.ValueKind == JsonValueKind.Object ? new[] { root } : Array.Empty<JsonElement>();
            return items.Select(x => new AdapterPowerStatus(
                GetString(x, "Name"), GetString(x, "InterfaceDescription"), GetString(x, "Status"),
                GetString(x, "LinkSpeed"), GetString(x, "SelectiveSuspend"), GetString(x, "DeviceSleepOnDisconnect")))
                .Where(x => !string.IsNullOrWhiteSpace(x.Name)).ToArray();
        }
        finally
        {
            try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
        }
    }

    public bool CanReducePowerSaving(AdapterPowerStatus adapter) =>
        string.Equals(adapter.SelectiveSuspend, "Enabled", StringComparison.OrdinalIgnoreCase)
        || string.Equals(adapter.DeviceSleepOnDisconnect, "Enabled", StringComparison.OrdinalIgnoreCase);

    public bool CanRestore(string adapterName) => File.Exists(GetBackupPath(adapterName));
    public IReadOnlyList<AdapterPowerBackup> GetPendingRestores()
    {
        if (!Directory.Exists(_backupDirectory)) return Array.Empty<AdapterPowerBackup>();
        var result = new List<AdapterPowerBackup>();
        foreach (var path in Directory.EnumerateFiles(_backupDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            AdapterPowerBackup backup;
            try
            {
                backup = JsonSerializer.Deserialize<AdapterPowerBackup>(File.ReadAllText(path), JsonOptions)
                    ?? throw new InvalidDataException("Backup de adaptador vazio.");
            }
            catch (Exception ex)
            {
                AppLog.Write("Backup de energia de adaptador inválido (" + Path.GetFileName(path) + "): " + ex.Message);
                throw new InvalidDataException("O backup de energia de adaptador está corrompido; ele foi preservado: " + path, ex);
            }

            ValidateName(backup.Name);
            if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(GetBackupPath(backup.Name)), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("O nome do adaptador não corresponde ao caminho do backup: " + path);
            if (!IsKnownPowerSetting(backup.SelectiveSuspend) || !IsKnownPowerSetting(backup.DeviceSleepOnDisconnect))
                throw new InvalidDataException("O backup contém um estado de energia desconhecido: " + path);
            result.Add(backup);
        }
        return result.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool IsKnownPowerSetting(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        (value.Equals("Enabled", StringComparison.OrdinalIgnoreCase)
        || value.Equals("Disabled", StringComparison.OrdinalIgnoreCase)
        || value.Equals("Unsupported", StringComparison.OrdinalIgnoreCase)
        || value.Equals("Unknown", StringComparison.OrdinalIgnoreCase));


    public void ReducePowerSaving(AdapterPowerStatus adapter)
    {
        ValidateName(adapter.Name);
        if (!CanReducePowerSaving(adapter))
            throw new InvalidOperationException("Este adaptador não apresenta opções compatíveis de economia de energia habilitadas.");

        var backupPath = GetBackupPath(adapter.Name);
        if (File.Exists(backupPath))
            throw new InvalidOperationException("Já existe um backup para este adaptador. Restaure-o antes de aplicar novamente.");

        var backup = new AdapterPowerBackup(adapter.Name, adapter.SelectiveSuspend,
            adapter.DeviceSleepOnDisconnect, DateTimeOffset.UtcNow);
        WriteAtomic(backupPath, JsonSerializer.Serialize(backup, JsonOptions));

        var targetSelective = string.Equals(adapter.SelectiveSuspend, "Enabled", StringComparison.OrdinalIgnoreCase) ? "Disabled" : adapter.SelectiveSuspend;
        var targetDeviceSleep = string.Equals(adapter.DeviceSleepOnDisconnect, "Enabled", StringComparison.OrdinalIgnoreCase) ? "Disabled" : adapter.DeviceSleepOnDisconnect;
        var script = BuildSetScript(adapter.Name, targetSelective, targetDeviceSleep,
            adapter.SelectiveSuspend, adapter.DeviceSleepOnDisconnect);
        try
        {
            RunElevated(script);
            AppLog.Write("Economia de energia do adaptador reduzida: " + adapter.Name);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            try { File.Delete(backupPath); } catch { }
            throw new OperationCanceledException("A elevação foi cancelada. Nenhuma alteração foi aplicada.", ex);
        }
    }

    public void Restore(string adapterName)
    {
        ValidateName(adapterName);
        var backupPath = GetBackupPath(adapterName);
        if (!File.Exists(backupPath))
            throw new InvalidOperationException("Não existe backup para este adaptador.");
        var backup = JsonSerializer.Deserialize<AdapterPowerBackup>(File.ReadAllText(backupPath), JsonOptions)
            ?? throw new InvalidDataException("Backup de energia do adaptador inválido.");
        if (!string.Equals(backup.Name, adapterName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("O nome do adaptador não corresponde ao backup.");

        var expectedSelective = IsSupported(backup.SelectiveSuspend) ? "Disabled" : backup.SelectiveSuspend;
        var expectedDeviceSleep = IsSupported(backup.DeviceSleepOnDisconnect) ? "Disabled" : backup.DeviceSleepOnDisconnect;
        RunElevated(BuildSetScript(adapterName, backup.SelectiveSuspend, backup.DeviceSleepOnDisconnect,
            expectedSelective, expectedDeviceSleep));

        File.Delete(backupPath);
        AppLog.Write("Configuração de energia do adaptador restaurada: " + adapterName);
    }

    private static string BuildSetScript(string name, string selectiveSuspend, string deviceSleep,
        string expectedSelective, string expectedDeviceSleep)
    {
        var n = EscapePowerShell(name);
        var s = EscapePowerShell(selectiveSuspend);
        var d = EscapePowerShell(deviceSleep);
        var expectedS = EscapePowerShell(expectedSelective);
        var expectedD = EscapePowerShell(expectedDeviceSleep);
        return "$ErrorActionPreference='Stop'; $n='" + n + "'; $s='" + s + "'; $d='" + d +
            "'; $es='" + expectedS + "'; $ed='" + expectedD + "'; try { " +
            "$before=Get-NetAdapterPowerManagement -Name $n -ErrorAction Stop; " +
            "if ($es -in @('Enabled','Disabled') -and [string]$before.SelectiveSuspend -ne $es) { throw 'SelectiveSuspend mudou desde a leitura' }; " +
            "if ($ed -in @('Enabled','Disabled') -and [string]$before.DeviceSleepOnDisconnect -ne $ed) { throw 'DeviceSleepOnDisconnect mudou desde a leitura' }; " +
            "$p=@{Name=$n;NoRestart=$true}; " +
            "if ($s -in @('Enabled','Disabled')) { $p.SelectiveSuspend=$s }; " +
            "if ($d -in @('Enabled','Disabled')) { $p.DeviceSleepOnDisconnect=$d }; " +
            "if ($p.Count -gt 2) { Set-NetAdapterPowerManagement @p -ErrorAction Stop }; " +
            "$after=Get-NetAdapterPowerManagement -Name $n -ErrorAction Stop; " +
            "if ($s -in @('Enabled','Disabled') -and [string]$after.SelectiveSuspend -ne $s) { throw 'SelectiveSuspend não confirmado' }; " +
            "if ($d -in @('Enabled','Disabled') -and [string]$after.DeviceSleepOnDisconnect -ne $d) { throw 'DeviceSleepOnDisconnect não confirmado' }; " +
            "} catch { exit 2 }; exit 0";
    }

    private static bool IsSupported(string value) =>
        value.Equals("Enabled", StringComparison.OrdinalIgnoreCase)
        || value.Equals("Disabled", StringComparison.OrdinalIgnoreCase);

    private string GetBackupPath(string name)
    {
        ValidateName(name);
        var canonical = Path.Combine(_backupDirectory, GetBackupFileName(name));
        // Preserve restore support for backups created by older ynoez versions.
        var legacy = Path.Combine(_backupDirectory,
            string.Concat(name.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_')) + ".json");
        return !File.Exists(canonical) && File.Exists(legacy) ? legacy : canonical;
    }

    public static string GetBackupFileName(string name)
    {
        ValidateName(name);
        var safe = string.Concat(name.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name))).ToLowerInvariant();
        return safe + "-" + hash[..12] + ".json";
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name.Any(char.IsControl))
            throw new ArgumentException("Nome de adaptador inválido.", nameof(name));
    }

    private static string GetString(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";

    private static string EscapePowerShell(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static void RunPowerShell(string script)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(AppPaths.PowerShellExecutable,
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new InvalidOperationException("A consulta de energia dos adaptadores falhou (código " +
                process.ExitCode + "). " + error + " " + output);
    }

    private static void RunElevated(string script)
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
            throw new InvalidOperationException("O Windows recusou a alteração do adaptador (código " + process.ExitCode + ").");
    }

    private static void WriteAtomic(string path, string content)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, overwrite: true);
    }
}
