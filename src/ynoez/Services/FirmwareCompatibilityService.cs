using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ynoez.Services;

public sealed record FirmwareDetectedSetting(string Name, string Value, IReadOnlyList<string> AllowedValues);
public sealed record FirmwareCompatibilitySnapshot(
    string BoardManufacturer,
    string BoardProduct,
    string Cpu,
    string BiosMode,
    string BiosVersion,
    bool SecureBootEnabled,
    bool TpmPresent,
    bool TpmReady,
    string TpmVersion,
    bool CpuVirtualizationEnabled,
    bool VbsEnabled,
    bool MemoryIntegrityEnabled,
    bool DmaProtectionAvailable,
    bool LenovoWmiAvailable,
    bool LenovoPasswordStateKnown,
    string? AsusActExecutable,
    bool LenovoSupervisorPasswordSet,
    bool BitLockerProtectionOn,
    IReadOnlyList<string> DetectedAntiCheats,
    IReadOnlyList<FirmwareDetectedSetting> FirmwareSettings)
{
    public bool AsusActAvailable => !string.IsNullOrWhiteSpace(AsusActExecutable);
    public bool CanWriteBios =>
        !BitLockerProtectionOn &&
        string.Equals(BiosMode, "UEFI", StringComparison.OrdinalIgnoreCase) &&
        ((LenovoWmiAvailable && LenovoPasswordStateKnown && !LenovoSupervisorPasswordSet) ||
         AsusActAvailable);
}

public sealed record FirmwareApplyResult(
    bool Applied,
    bool RequiresRestart,
    int ChangedSettings,
    IReadOnlyList<string> ChangedNames,
    string Message);

/// <summary>
/// Detects boot/security requirements and uses the documented Lenovo WMI BIOS
/// interface only on supported Lenovo systems. It never writes raw UEFI NVRAM
/// offsets or guesses settings for an unknown motherboard.
/// </summary>
public static class FirmwareCompatibilityService
{
    private const string WmiNamespace = @"\\.\root\WMI";
    private static readonly string JournalDirectory = Path.Combine(AppPaths.Data, "Firmware");

    private static readonly IReadOnlyDictionary<string, string[]> RequirementMap =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["faceit"] = ["secureboot", "tpm", "iommu", "virtualization"],
            ["riot-vanguard"] = ["secureboot", "tpm"],
            ["vanguard-precheck"] = ["secureboot", "tpm", "iommu", "virtualization"],
            ["easyanticheat"] = [],
            ["battleye"] = []
        };

    private static readonly IReadOnlyDictionary<string, string[]> SettingNameTokens =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["secureboot"] = ["secureboot"],
            ["tpm"] = ["securitychip", "securitydevice", "tpm", "ftpm", "ptt"],
            ["iommu"] = ["iommu", "vtd", "vt-d", "amdvi", "dmaremapping"],
            ["virtualization"] = ["intelvirtualizationtechnology", "svm", "virtualizationtechnology", "amdvirtualization"],
            ["cpu-boost"] = ["coreperformanceboost", "cpb", "intelturboboosttechnology", "turbomode", "turboboost"]
        };

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFirmwareType(out uint firmwareType);

    public static Task<FirmwareCompatibilitySnapshot> InspectAsync(HardwareSnapshot? hardware = null) =>
        Task.Run(() => Inspect(hardware ?? HardwareService.GetSnapshot()));

    public static FirmwareCompatibilitySnapshot Inspect(HardwareSnapshot hardware)
    {
        ArgumentNullException.ThrowIfNull(hardware);

        var firmwareType = GetFirmwareType(out var type)
            ? type == 2 ? "UEFI" : type == 1 ? "Legacy" : "Desconhecido"
            : "Desconhecido";
        var secureBoot = ReadSecureBoot();
        var tpm = ReadTpm();
        var deviceGuard = ReadDeviceGuard();
        var virtualization = ReadCpuVirtualization();
        var antiCheats = DetectAntiCheats();
        var manufacturer = Normalize(hardware.BoardManufacturer);
        var lenovoAvailable = false;
        IReadOnlyList<FirmwareDetectedSetting> lenovoSettings = Array.Empty<FirmwareDetectedSetting>();
        var passwordStateKnown = false;
        var passwordSet = false;
        if (manufacturer.Contains("LENOVO", StringComparison.Ordinal))
        {
            lenovoSettings = TryReadLenovoSettings(out lenovoAvailable);
            passwordSet = lenovoAvailable && ReadLenovoSupervisorPasswordState(out passwordStateKnown);
        }
        var passwordStateConfirmed = lenovoAvailable && passwordStateKnown;

        AsusFirmwareInspection? asus = null;
        if (manufacturer.Contains("ASUS", StringComparison.Ordinal))
            AsusConfigurationToolAdapter.TryInspect(out asus);
        var firmwareSettings = asus?.Settings ?? lenovoSettings;

        return new FirmwareCompatibilitySnapshot(
            hardware.BoardManufacturer,
            hardware.BoardProduct,
            hardware.Cpu,
            firmwareType,
            hardware.BiosVersion,
            secureBoot,
            tpm.Present,
            tpm.Ready,
            tpm.Version,
            virtualization,
            deviceGuard.Vbs,
            deviceGuard.Hvci,
            deviceGuard.Dma,
            lenovoAvailable,
            passwordStateConfirmed,
            asus?.ExecutablePath,
            passwordSet,
            IsBitLockerProtectionOn(),
            antiCheats,
            firmwareSettings);
    }

    public static string BuildManualGuidance(
        FirmwareCompatibilitySnapshot snapshot,
        IEnumerable<string> selectedProfiles,
        IReadOnlyList<string>? requiredSettings = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(selectedProfiles);
        var required = requiredSettings ?? GetRequiredSettingIds(selectedProfiles);
        var labels = required.Select(id => id switch
        {
            "secureboot" => "Secure Boot",
            "tpm" => "TPM 2.0 / AMD fTPM",
            "iommu" => "IOMMU / DMA Remapping",
            "virtualization" => "SVM / Intel VT-x",
            "cpu-boost" => "Core Performance Boost / Intel Turbo Boost",
            _ => id
        }).ToArray();

        var board = Normalize(snapshot.BoardManufacturer + " " + snapshot.BoardProduct);
        var lines = new List<string>();
        if (board.Contains("GIGABYTEA520MKV2", StringComparison.Ordinal))
        {
            lines.Add("GIGABYTE A520M K V2 — entre em Advanced Mode:");
            if (required.Contains("tpm", StringComparer.Ordinal))
                lines.Add("Settings > AMD CPU fTPM: Enabled.");
            if (required.Contains("virtualization", StringComparer.Ordinal))
                lines.Add("SVM Mode: Enabled (localize em AMD CBS/CPU Common Options; o menu varia por BIOS).");
            if (required.Contains("iommu", StringComparer.Ordinal))
                lines.Add("IOMMU: Enabled, se a BIOS/CPU expuser essa opção.");
            if (required.Contains("secureboot", StringComparer.Ordinal))
            {
                lines.Add(snapshot.BiosMode.Equals("UEFI", StringComparison.OrdinalIgnoreCase)
                    ? "Boot > CSM Support: Disabled; depois Secure Boot > Custom > Restore Factory Keys; confirme Secure Boot Active."
                    : "Secure Boot bloqueado: o Windows iniciou em Legacy. Não desative CSM; converta o disco do sistema para GPT e valide UEFI antes.");
            }
            lines.Add("CPU: Core Performance Boost em Auto/Enabled. PBO, tensão, clocks e limites térmicos ficam no padrão.");
        }
        else
        {
            lines.Add("Esta BIOS não oferece uma API de gravação suportada pelo ynoez.");
            lines.Add("Recursos necessários: " + string.Join(", ", labels) + ".");
        }
        lines.Add("Nenhum valor foi gravado. O ynoez não usa offsets UEFI genéricos, não altera tensão/overclock e não desativa proteções.");
        return string.Join("\n", lines);
    }

    public static IReadOnlyList<string> GetRequiredSettingIds(IEnumerable<string> selectedProfiles)
    {
        ArgumentNullException.ThrowIfNull(selectedProfiles);
        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "cpu-boost"
        };

        foreach (var profile in selectedProfiles)
        {
            if (!RequirementMap.TryGetValue(profile, out var values))
                throw new ArgumentException("Perfil de anti-cheat desconhecido: " + profile, nameof(selectedProfiles));
            foreach (var value in values) required.Add(value);
        }

        return required.OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }

    public static string? FindSettingName(IEnumerable<string> firmwareSettingNames, string requirementId)
    {
        ArgumentNullException.ThrowIfNull(firmwareSettingNames);
        if (!SettingNameTokens.TryGetValue(requirementId, out var tokens))
            throw new ArgumentOutOfRangeException(nameof(requirementId));

        foreach (var setting in firmwareSettingNames)
        {
            var normalized = Normalize(setting);
            if (tokens.Any(token => normalized.Contains(Normalize(token), StringComparison.Ordinal)))
                return setting;
        }
        return null;
    }

    public static string? SelectEnabledValue(IEnumerable<string> allowedValues, string requirementId = "")
    {
        ArgumentNullException.ThrowIfNull(allowedValues);
        string[] preferred = string.Equals(requirementId, "cpu-boost", StringComparison.OrdinalIgnoreCase)
            ? ["Enabled", "Enable", "Auto", "On", "Active", "1"]
            : ["Enabled", "Enable", "On", "Active", "1"];
        var values = allowedValues.ToArray();
        return preferred.FirstOrDefault(target =>
            values.Any(value => string.Equals(value.Trim(), target, StringComparison.OrdinalIgnoreCase))) is { } match
            ? values.First(value => string.Equals(value.Trim(), match, StringComparison.OrdinalIgnoreCase))
            : null;
    }

    public static FirmwareApplyResult ApplySelectedProfiles(
        FirmwareCompatibilitySnapshot snapshot,
        IEnumerable<string> selectedProfiles)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(selectedProfiles);

        var selected = selectedProfiles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var targets = GetRequiredSettingIds(selected);
        if (targets.Count == 0)
            return new FirmwareApplyResult(false, false, 0, [], "Nenhum ajuste de firmware foi solicitado.");

        if (!snapshot.LenovoWmiAvailable && !snapshot.AsusActAvailable)
            return new FirmwareApplyResult(false, false, 0, [],
                BuildManualGuidance(snapshot, selected, targets));
        if (!snapshot.CanWriteBios)
        {
            var reason = snapshot.LenovoWmiAvailable && !snapshot.LenovoPasswordStateKnown
                ? "Não foi possível confirmar o estado da senha de administrador da BIOS; a gravação foi bloqueada por segurança."
                : snapshot.LenovoSupervisorPasswordSet
                    ? "A BIOS possui senha de administrador; o ynoez não tenta contornar nem armazenar essa senha."
                    : snapshot.BitLockerProtectionOn
                        ? "A proteção do BitLocker está ativa ou seu estado é desconhecido. A gravação foi bloqueada para evitar problemas com TPM/recuperação."
                        : !string.Equals(snapshot.BiosMode, "UEFI", StringComparison.OrdinalIgnoreCase)
                            ? "O Windows iniciou em modo Legacy. A troca para UEFI não será feita automaticamente porque pode impedir a inicialização."
                            : "A BIOS não está liberada para gravação segura.";
            return new FirmwareApplyResult(false, false, 0, [], reason);
        }

        if (snapshot.AsusActAvailable && !snapshot.LenovoWmiAvailable)
            return AsusConfigurationToolAdapter.Apply(snapshot.AsusActExecutable!, targets);

        var current = ReadLenovoSettings();
        var missing = GetMissingRequirementIds(current, targets);
        if (missing.Count > 0)
            return new FirmwareApplyResult(false, false, 0, [],
                "A BIOS Lenovo não expôs opções seguras para todos os requisitos: " +
                string.Join(", ", missing) + ". Nenhuma configuração foi alterada.");
        var changes = BuildLenovoChangePlan(current, targets);
        if (changes.Count == 0)
            return new FirmwareApplyResult(false, false, 0, [],
                "Os recursos necessários já estão habilitados na BIOS. Nenhuma alteração foi necessária.");

        Directory.CreateDirectory(JournalDirectory);
        var journalPath = Path.Combine(JournalDirectory, "lenovo-bios-pending.json");
        if (File.Exists(journalPath) && !ResolveLenovoPendingJournal(journalPath, current))
            throw new InvalidOperationException("Há uma configuração Lenovo pendente, parcialmente aplicada ou não confirmada. Reinicie e analise novamente antes de aplicar outro perfil.");

        var journal = new
        {
            CapturedAt = DateTimeOffset.UtcNow,
            Board = snapshot.BoardManufacturer + " " + snapshot.BoardProduct,
            BiosVersion = snapshot.BiosVersion,
            Changes = changes.Select(x => new { x.Name, Original = x.OriginalValue, Target = x.TargetValue }).ToArray()
        };
        WriteAtomic(journalPath, JsonSerializer.Serialize(journal, new JsonSerializerOptions { WriteIndented = true }));

        var staged = new List<LenovoChange>();
        try
        {
            foreach (var change in changes)
            {
                var status = InvokeLenovoMethod("Lenovo_SetBiosSetting", "SetBiosSetting",
                    "parameter", change.Name + "," + change.TargetValue);
                if (!string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("A BIOS recusou " + change.Name + ": " + status);
                staged.Add(change);
            }

            var saveStatus = InvokeLenovoMethod("Lenovo_SaveBiosSettings", "SaveBiosSettings", null, null);
            if (!string.Equals(saveStatus, "Success", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A BIOS não confirmou o salvamento: " + saveStatus);

            AppLog.Write("Perfil BIOS Lenovo preparado: " +
                string.Join(", ", changes.Select(x => x.Name + "=" + x.TargetValue)) +
                ". Reinicialização necessária.");
            return new FirmwareApplyResult(true, true, changes.Count,
                changes.Select(x => x.Name).ToArray(),
                "Configurações compatíveis gravadas pela interface WMI oficial da Lenovo. Reinicie para efetivá-las; depois execute Analisar novamente.");
        }
        catch
        {
            try
            {
                InvokeLenovoMethod("Lenovo_DiscardBiosSettings", "DiscardBiosSettings", null, null);
                File.Delete(journalPath);
            }
            catch (Exception discardError)
            {
                AppLog.Write("A BIOS não confirmou descarte das alterações pendentes: " + discardError.Message);
            }
            throw;
        }
    }

    private sealed record LenovoChange(string Name, string OriginalValue, string TargetValue);

    private static IReadOnlyList<string> GetMissingRequirementIds(
        IReadOnlyList<FirmwareDetectedSetting> settings,
        IReadOnlyList<string> requirementIds)
    {
        var missing = new List<string>();
        foreach (var requirement in requirementIds)
        {
            var name = FindSettingName(settings.Select(x => x.Name), requirement);
            if (name is null)
            {
                missing.Add(requirement);
                continue;
            }
            var setting = settings.First(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            var alreadyEnabled =
                string.Equals(setting.Value, "Enabled", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(setting.Value, "Enable", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(setting.Value, "On", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(setting.Value, "Active", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(setting.Value, "1", StringComparison.OrdinalIgnoreCase) ||
                (requirement == "cpu-boost" && string.Equals(setting.Value, "Auto", StringComparison.OrdinalIgnoreCase));
            if (!alreadyEnabled && SelectEnabledValue(setting.AllowedValues, requirement) is null)
                missing.Add(requirement);
        }
        return missing;
    }

    private static bool ResolveLenovoPendingJournal(
        string journalPath,
        IReadOnlyList<FirmwareDetectedSetting> currentSettings)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(journalPath));
            if (!document.RootElement.TryGetProperty("Changes", out var changes) ||
                changes.ValueKind != JsonValueKind.Array || changes.GetArrayLength() == 0)
                return false;

            var allTarget = true;
            var allOriginal = true;
            foreach (var change in changes.EnumerateArray())
            {
                var name = change.GetProperty("Name").GetString();
                var target = change.GetProperty("Target").GetString();
                var original = change.GetProperty("Original").GetString();
                var current = currentSettings.FirstOrDefault(x =>
                    string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (current is null) return false;
                allTarget &= string.Equals(current.Value, target, StringComparison.OrdinalIgnoreCase);
                allOriginal &= string.Equals(current.Value, original, StringComparison.OrdinalIgnoreCase);
            }

            if (!allTarget && !allOriginal) return false;
            File.Delete(journalPath);
            AppLog.Write(allTarget
                ? "Lenovo BIOS confirmou os ajustes após reinicialização."
                : "Lenovo BIOS permaneceu nos valores originais; a tentativa anterior não foi aplicada.");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write("Journal Lenovo BIOS não pôde ser reconciliado: " + ex.Message);
            return false;
        }
    }

    private static List<LenovoChange> BuildLenovoChangePlan(
        IReadOnlyList<FirmwareDetectedSetting> settings,
        IReadOnlyList<string> requirementIds)
    {
        var plan = new List<LenovoChange>();
        foreach (var requirement in requirementIds)
        {
            var name = FindSettingName(settings.Select(x => x.Name), requirement);
            if (name is null) continue;
            var current = settings.First(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            var enabled = SelectEnabledValue(current.AllowedValues, requirement);
            var alreadyEnabled = string.Equals(current.Value, "Enabled", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current.Value, "Enable", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current.Value, "On", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current.Value, "Active", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current.Value, "1", StringComparison.OrdinalIgnoreCase) ||
                (requirement == "cpu-boost" && string.Equals(current.Value, "Auto", StringComparison.OrdinalIgnoreCase));
            if (enabled is null || alreadyEnabled)
                continue;
            plan.Add(new LenovoChange(current.Name, current.Value, enabled));
        }
        return plan;
    }

    private static IReadOnlyList<FirmwareDetectedSetting> TryReadLenovoSettings(out bool available)
    {
        try
        {
            var settings = ReadLenovoSettings();
            available = settings.Count > 0;
            return settings;
        }
        catch (Exception ex)
        {
            available = false;
            AppLog.Write("Interface Lenovo BIOS WMI indisponível: " + ex.Message);
            return Array.Empty<FirmwareDetectedSetting>();
        }
    }

    private static IReadOnlyList<FirmwareDetectedSetting> ReadLenovoSettings()
    {
        var result = new List<FirmwareDetectedSetting>();
        var scope = new ManagementScope(WmiNamespace);
        scope.Connect();
        using var searcher = new ManagementObjectSearcher(scope,
            new ObjectQuery("SELECT CurrentSetting FROM Lenovo_BiosSetting"));
        using var rows = searcher.Get();
        foreach (ManagementObject row in rows)
        {
            using (row)
            {
                var current = Convert.ToString(row["CurrentSetting"]) ?? "";
                var comma = current.IndexOf(',');
                if (comma <= 0) continue;
                var name = current[..comma].Trim();
                var value = current[(comma + 1)..].Trim();
                if (name.Length == 0) continue;
                var allowed = ReadLenovoSelections(name);
                result.Add(new FirmwareDetectedSetting(name, value, allowed));
            }
        }
        return result;
    }

    private static IReadOnlyList<string> ReadLenovoSelections(string setting)
    {
        var scope = new ManagementScope(WmiNamespace);
        using var cls = new ManagementClass(scope, new ManagementPath("Lenovo_GetBiosSelections"), null);
        using var input = cls.GetMethodParameters("GetBiosSelections");
        input["Item"] = setting;
        using var output = cls.InvokeMethod("GetBiosSelections", input, null);
        var selections = Convert.ToString(output?["Selections"]) ?? "";
        return selections.Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string InvokeLenovoMethod(
        string className,
        string methodName,
        string? argumentName,
        string? argumentValue)
    {
        var scope = new ManagementScope(WmiNamespace);
        using var cls = new ManagementClass(scope, new ManagementPath(className), null);
        using var input = cls.GetMethodParameters(methodName);
        if (!string.IsNullOrWhiteSpace(argumentName))
            input[argumentName] = argumentValue ?? "";
        using var output = cls.InvokeMethod(methodName, input, null);
        return Convert.ToString(output?["return"]) ?? "Unknown";
    }

    private static bool ReadLenovoSupervisorPasswordState(out bool known)
    {
        known = false;
        try
        {
            var scope = new ManagementScope(WmiNamespace);
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT PasswordState FROM Lenovo_BiosPasswordSettings"));
            using var rows = searcher.Get();
            foreach (ManagementObject row in rows)
            {
                using (row)
                {
                    if (row["PasswordState"] is null) return false;
                    var state = Convert.ToInt32(row["PasswordState"]);
                    known = true;
                    return (state & 2) != 0 || (state & 128) != 0;
                }
            }
        }
        catch (Exception ex) { AppLog.Write("Estado de senha Lenovo indisponível: " + ex.Message); }
        return false;
    }

    public static bool IsVanguardPreCheckSupportedOS(HardwareSnapshot hardware)
    {
        ArgumentNullException.ThrowIfNull(hardware);
        if (!hardware.OperatingSystem.Contains("Windows 11", StringComparison.OrdinalIgnoreCase))
            return false;
        var buildText = hardware.WindowsBuild.Split([' ', '('], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return int.TryParse(buildText, out var build) && build >= 26200;
    }

    private static bool ReadSecureBoot()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(AppPaths.PowerShellExecutable,
                "-NoProfile -NonInteractive -Command \"try { if (Confirm-SecureBootUEFI -ErrorAction Stop) { 'True' } else { 'False' } } catch { 'Unknown' }\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (process is null) return false;
            var output = process.StandardOutput.ReadToEnd().Trim();
            if (!process.WaitForExit(5000))
            {
                process.Kill(true);
                return false;
            }
            return string.Equals(output, "True", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) { AppLog.Write("Secure Boot não pôde ser consultado: " + ex.Message); return false; }
    }

    private sealed record TpmStatus(bool Present, bool Ready, string Version);

    private static TpmStatus ReadTpm()
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\CIMV2\Security\MicrosoftTpm");
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT IsEnabled_InitialValue, IsActivated_InitialValue, SpecVersion FROM Win32_Tpm"));
            using var rows = searcher.Get();
            foreach (ManagementObject row in rows)
            {
                using (row)
                {
                    var enabled = Convert.ToBoolean(row["IsEnabled_InitialValue"] ?? false);
                    var activated = Convert.ToBoolean(row["IsActivated_InitialValue"] ?? false);
                    var version = Convert.ToString(row["SpecVersion"]) ?? "";
                    var majorVersion = version.Split(',', ';')[0].Trim();
                    return new TpmStatus(true, enabled && activated, majorVersion);
                }
            }
        }
        catch (Exception ex) { AppLog.Write("TPM não pôde ser consultado: " + ex.Message); }
        return new TpmStatus(false, false, "—");
    }

    private sealed record DeviceGuardStatus(bool Vbs, bool Hvci, bool Dma);

    private static DeviceGuardStatus ReadDeviceGuard()
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\DeviceGuard");
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT VirtualizationBasedSecurityStatus, SecurityServicesRunning, AvailableSecurityProperties FROM Win32_DeviceGuard"));
            using var rows = searcher.Get();
            foreach (ManagementObject row in rows)
            {
                using (row)
                {
                    var vbs = Convert.ToInt32(row["VirtualizationBasedSecurityStatus"] ?? 0) == 2;
                    var running = ToIntSet(row["SecurityServicesRunning"]);
                    var available = ToIntSet(row["AvailableSecurityProperties"]);
                    return new DeviceGuardStatus(vbs, running.Contains(2), available.Contains(3));
                }
            }
        }
        catch (Exception ex) { AppLog.Write("Device Guard não pôde ser consultado: " + ex.Message); }
        return new DeviceGuardStatus(false, false, false);
    }

    private static HashSet<int> ToIntSet(object? value)
    {
        var result = new HashSet<int>();
        if (value is Array array)
            foreach (var item in array)
                if (int.TryParse(Convert.ToString(item), out var parsed)) result.Add(parsed);
        return result;
    }

    private static bool ReadCpuVirtualization()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT VirtualizationFirmwareEnabled FROM Win32_Processor");
            using var rows = searcher.Get();
            foreach (ManagementObject row in rows)
            {
                using (row)
                    if (Convert.ToBoolean(row["VirtualizationFirmwareEnabled"] ?? false)) return true;
            }
        }
        catch (Exception ex) { AppLog.Write("Virtualização de CPU não pôde ser consultada: " + ex.Message); }
        return false;
    }

    private static IReadOnlyList<string> DetectAntiCheats()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name FROM Win32_Service WHERE Name IN ('vgc','vgk','FACEITService','FACEITAC','EasyAntiCheat','EasyAntiCheat_EOS','BEService','BEService_x64')");
            using var rows = searcher.Get();
            foreach (ManagementObject row in rows)
            {
                using (row)
                {
                    var service = Convert.ToString(row["Name"]) ?? "";
                    if (service.StartsWith("FACEIT", StringComparison.OrdinalIgnoreCase)) names.Add("faceit");
                    else if (service.StartsWith("vg", StringComparison.OrdinalIgnoreCase)) names.Add("riot-vanguard");
                    else if (service.StartsWith("EasyAntiCheat", StringComparison.OrdinalIgnoreCase)) names.Add("easyanticheat");
                    else if (service.StartsWith("BE", StringComparison.OrdinalIgnoreCase)) names.Add("battleye");
                }
            }
        }
        catch (Exception ex) { AppLog.Write("Detecção de anti-cheat por serviço falhou: " + ex.Message); }

        var programFiles = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
        };
        foreach (var root in programFiles.Where(Directory.Exists))
        {
            if (Directory.Exists(Path.Combine(root, "Riot Vanguard"))) names.Add("riot-vanguard");
            if (Directory.Exists(Path.Combine(root, "FACEIT"))) names.Add("faceit");
            if (Directory.Exists(Path.Combine(root, "EasyAntiCheat"))) names.Add("easyanticheat");
            if (Directory.Exists(Path.Combine(root, "BattlEye"))) names.Add("battleye");
        }
        return names.OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }

    private static bool IsBitLockerProtectionOn()
    {
        try
        {
            var systemDrive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
            using var searcher = new ManagementObjectSearcher(
                @"\\.\root\CIMV2\Security\MicrosoftVolumeEncryption",
                "SELECT DriveLetter, ProtectionStatus FROM Win32_EncryptableVolume");
            using var rows = searcher.Get();
            foreach (ManagementObject volume in rows)
            {
                using (volume)
                {
                    var driveLetter = Convert.ToString(volume["DriveLetter"]);
                    if (!string.Equals(driveLetter, systemDrive, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Win32_EncryptableVolume: 0 = protection off, 1 = on, 2 = unknown.
                    // Unknown is treated as protected so an unattended firmware change
                    // cannot alter TPM/Secure Boot measurements while BitLocker may be active.
                    var status = Convert.ToInt32(volume["ProtectionStatus"] ?? 2);
                    return status != 0;
                }
            }

            AppLog.Write("Volume de sistema não apareceu na consulta BitLocker; alterações de BIOS bloqueadas.");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write("Estado BitLocker desconhecido; alterações de firmware bloqueadas: " + ex.Message);
            return true;
        }
    }

    private static string Normalize(string value) =>
        new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static void WriteAtomic(string path, string content)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, overwrite: true);
    }
}
