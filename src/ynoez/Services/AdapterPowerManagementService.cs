using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
        var script = "$ErrorActionPreference='Stop'; $items = @(Get-NetAdapter -Physical | ForEach-Object { " +
            "$a=$_; try { $p=Get-NetAdapterPowerManagement -Name $a.Name -ErrorAction Stop; " +
            "[pscustomobject]@{ Name=$a.Name; InterfaceDescription=$a.InterfaceDescription; Status=[string]$a.Status; " +
            "LinkSpeed=[string]$a.LinkSpeed; SelectiveSuspend=[string]$p.SelectiveSuspend; " +
            "DeviceSleepOnDisconnect=[string]$p.DeviceSleepOnDisconnect } } catch {} }); " +
            "$items | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath '" + path + "' -Encoding UTF8";
        RunElevated(script);

        try
        {
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
        IsSupported(adapter.SelectiveSuspend) && adapter.SelectiveSuspend == "Enabled"
        || IsSupported(adapter.DeviceSleepOnDisconnect) && adapter.DeviceSleepOnDisconnect == "Enabled";

    public bool CanRestore(string adapterName) => File.Exists(GetBackupPath(adapterName));

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

        var script = BuildSetScript(adapter.Name,
            adapter.SelectiveSuspend == "Enabled" ? "Disabled" : adapter.SelectiveSuspend,
            adapter.DeviceSleepOnDisconnect == "Enabled" ? "Disabled" : adapter.DeviceSleepOnDisconnect);
        try
        {
            RunElevated(script);
            var after = GetSingleAdapter(adapter.Name);
            if ((backup.SelectiveSuspend == "Enabled" && after.SelectiveSuspend != "Disabled")
                || (backup.DeviceSleepOnDisconnect == "Enabled" && after.DeviceSleepOnDisconnect != "Disabled"))
                throw new InvalidOperationException("O driver não confirmou a alteração. O backup foi mantido.");
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

        RunElevated(BuildSetScript(adapterName, backup.SelectiveSuspend, backup.DeviceSleepOnDisconnect));
        var after = GetSingleAdapter(adapterName);
        if (IsSupported(backup.SelectiveSuspend) && after.SelectiveSuspend != backup.SelectiveSuspend
            || IsSupported(backup.DeviceSleepOnDisconnect) && after.DeviceSleepOnDisconnect != backup.DeviceSleepOnDisconnect)
            throw new InvalidOperationException("A restauração do adaptador não foi confirmada; o backup foi mantido.");

        File.Delete(backupPath);
        AppLog.Write("Configuração de energia do adaptador restaurada: " + adapterName);
    }

    private AdapterPowerStatus GetSingleAdapter(string name) =>
        GetPhysicalAdapters().FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException("O adaptador não foi encontrado após a alteração.");

    private static string BuildSetScript(string name, string selectiveSuspend, string deviceSleep)
    {
        var n = EscapePowerShell(name);
        var s = EscapePowerShell(selectiveSuspend);
        var d = EscapePowerShell(deviceSleep);
        return "$ErrorActionPreference='Stop'; $n='" + n + "'; $p=@{Name=$n;NoRestart=$true}; " +
            "if ('" + s + "' -in @('Enabled','Disabled')) { $p.SelectiveSuspend='" + s + "' }; " +
            "if ('" + d + "' -in @('Enabled','Disabled')) { $p.DeviceSleepOnDisconnect='" + d + "' }; " +
            "if ($p.Count -gt 2) { Set-NetAdapterPowerManagement @p -ErrorAction Stop }; exit 0";
    }

    private static bool IsSupported(string value) =>
        value.Equals("Enabled", StringComparison.OrdinalIgnoreCase)
        || value.Equals("Disabled", StringComparison.OrdinalIgnoreCase);

    private string GetBackupPath(string name)
    {
        ValidateName(name);
        var safe = string.Concat(name.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return Path.Combine(_backupDirectory, safe + ".json");
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128
            || name.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is ' ' or '-' or '_' or '(' or ')')))
            throw new ArgumentException("Nome de adaptador inválido.", nameof(name));
    }

    private static string GetString(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";

    private static string EscapePowerShell(string value) => value.Replace("'", "''", StringComparison.Ordinal);

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
            throw new InvalidOperationException("O Windows recusou a alteração do adaptador (código " + process.ExitCode + ").");
    }

    private static void WriteAtomic(string path, string content)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, overwrite: true);
    }
}
