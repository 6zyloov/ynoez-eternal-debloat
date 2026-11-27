using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ynoez.Services;

public enum PowerProfileMode { Performance, Balanced, Afk }

public sealed record PowerProfileState(string Mode, string AppliedGuid, DateTimeOffset AppliedAt);
public sealed record PowerProfileBackup(string OriginalGuid, DateTimeOffset SavedAt, string? ExportPath = null);
public sealed record PowerProfileSession(string OriginalGuid, string ExportPath, DateTimeOffset StartedAt);
public sealed record PowerProfileResolution(PowerProfileMode RequestedMode, PowerProfileMode AppliedMode, string RequestedAlias, string AppliedAlias, string Guid)
{
    public bool UsedFallback => RequestedMode != AppliedMode;
}

public static class PowerProfileService
{
    private const uint PowerRequestContextSimpleString = 0x00000001;
    private static readonly object PowerRequestSync = new();
    private static IntPtr _systemSleepRequest = IntPtr.Zero;
    private static readonly string BaseBackupPath = Path.Combine(AppPaths.Data, "power-profile-base.json");
    private static readonly string StatePath = Path.Combine(AppPaths.Data, "power-profile-state.json");
    private static readonly string SessionPath = Path.Combine(AppPaths.Data, "auto-profile-session.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly Regex GuidPattern = new(@"^[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$", RegexOptions.Compiled);

    private enum PowerRequestType
    {
        DisplayRequired = 0,
        SystemRequired = 1,
        AwayModeRequired = 2,
        ExecutionRequired = 3
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerRequestContext
    {
        public uint Version;
        public uint Flags;
        public IntPtr SimpleReasonString;
    }

    [DllImport("powrprof.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr PowerCreateRequest(ref PowerRequestContext context);

    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PowerSetRequest(IntPtr requestHandle, PowerRequestType requestType);

    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PowerClearRequest(IntPtr requestHandle, PowerRequestType requestType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    public static bool HasInterruptedAutomaticSession => File.Exists(SessionPath);
    public static bool CanRestoreOriginal() => File.Exists(BaseBackupPath);

    public static string GetActiveSchemeGuid()
    {
        var result = RunPowerCfg("/getactivescheme");
        var match = Regex.Match(result.Output, @"[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}");
        if (result.ExitCode != 0 || !match.Success)
            throw new InvalidOperationException("Não foi possível identificar o plano de energia ativo: " + result.Error);
        return match.Value;
    }

    public static string Apply(PowerProfileMode mode) => ApplyCore(mode, createPersistentBackup: true, persistState: true);

    public static string ApplyForAutomaticSession(PowerProfileMode mode) =>
        ApplyCore(mode, createPersistentBackup: false, persistState: false);

    private static string ApplyCore(PowerProfileMode mode, bool createPersistentBackup, bool persistState)
    {
        var resolution = ResolveTarget(mode);
        var targetGuid = resolution.Guid;
        var current = GetActiveSchemeGuid();
        if (string.Equals(current, targetGuid, StringComparison.OrdinalIgnoreCase))
        {
            SetExecutionRequirement(mode);
            return DescribeAppliedProfile(resolution, alreadyActive: true);
        }

        if (createPersistentBackup)
            EnsurePersistentBackup(current);

        var result = RunPowerCfg("/setactive " + targetGuid);
        if (result.ExitCode != 0)
            throw new InvalidOperationException("O Windows não conseguiu alterar o plano de energia: " + result.Error);

        var active = GetActiveSchemeGuid();
        if (!string.Equals(active, targetGuid, StringComparison.OrdinalIgnoreCase))
        {
            TrySetActive(current);
            throw new InvalidOperationException("O Windows não confirmou o plano solicitado; o plano anterior foi recuperado quando possível.");
        }

        try
        {
            SetExecutionRequirement(mode);
            if (persistState)
            {
                var state = new PowerProfileState(resolution.AppliedMode.ToString(), active, DateTimeOffset.UtcNow);
                WriteAtomic(StatePath, JsonSerializer.Serialize(state, JsonOptions));
            }
        }
        catch
        {
            TrySetActive(current);
            TryClearExecutionRequirement();
            throw;
        }

        AppLog.Write("Perfil solicitado: " + mode + "; plano aplicado: " + resolution.AppliedMode + " (" + active + ")" +
            (mode == PowerProfileMode.Afk ? "; suspensão automática bloqueada enquanto o ynoez estiver aberto." : ""));
        return DescribeAppliedProfile(resolution, alreadyActive: false);
    }

    private static string DescribeAppliedProfile(PowerProfileResolution resolution, bool alreadyActive)
    {
        if (resolution.UsedFallback)
        {
            if (resolution.RequestedMode == PowerProfileMode.Afk)
                return "Este Windows não disponibilizou o plano Economia (SCHEME_MAX). O plano Equilibrado foi usado no lugar; a suspensão automática do sistema ficará bloqueada enquanto o ynoez estiver aberto. A tela pode apagar normalmente.";
            return "Este Windows não disponibilizou o plano Alto desempenho (SCHEME_MIN). O plano Equilibrado foi mantido. Nenhum ajuste de clock, tensão ou BIOS foi feito.";
        }

        if (alreadyActive)
            return "O plano " + resolution.AppliedMode + " já estava ativo. A política de suspensão foi atualizada conforme o perfil.";

        return resolution.AppliedMode switch
        {
            PowerProfileMode.Performance => "Perfil Alto desempenho ativado. O plano original está exportado para restauração.",
            PowerProfileMode.Balanced => "Perfil Equilibrado ativado. O plano original está exportado para restauração.",
            PowerProfileMode.Afk => "Perfil AFK ativado. O sistema não entrará em suspensão automática enquanto o ynoez estiver aberto; a tela pode apagar conforme o plano do Windows.",
            _ => "Perfil aplicado."
        };
    }

    private static void EnsurePersistentBackup(string current)
    {
        if (File.Exists(BaseBackupPath))
        {
            var existing = JsonSerializer.Deserialize<PowerProfileBackup>(File.ReadAllText(BaseBackupPath), JsonOptions)
                ?? throw new InvalidDataException("Backup do plano base inválido; a alteração foi bloqueada.");
            if (!GuidPattern.IsMatch(existing.OriginalGuid))
                throw new InvalidDataException("GUID no backup do plano base inválido; a alteração foi bloqueada.");
            return;
        }

        Directory.CreateDirectory(AppPaths.Backups);
        var exportPath = Path.Combine(AppPaths.Backups,
            "power-profile-base-" + current + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".pow");
        var export = RunPowerCfg("/export " + Quote(exportPath) + " " + current);
        if (export.ExitCode != 0 || !File.Exists(exportPath))
            throw new InvalidOperationException("Não foi possível exportar o plano original antes da alteração. Nenhum perfil foi aplicado. " + export.Error);
        WriteAtomic(BaseBackupPath,
            JsonSerializer.Serialize(new PowerProfileBackup(current, DateTimeOffset.UtcNow, exportPath), JsonOptions));
    }

    public static PowerProfileSession CaptureAutomaticSession()
    {
        if (File.Exists(SessionPath))
            throw new InvalidOperationException("Existe uma sessão automática anterior sem restauração. Restaure-a antes de iniciar outra.");
        Directory.CreateDirectory(AppPaths.Backups);
        Directory.CreateDirectory(AppPaths.Data);
        var originalGuid = GetActiveSchemeGuid();
        var exportPath = Path.Combine(AppPaths.Backups, "auto-profile-original-" + Guid.NewGuid().ToString("N") + ".pow");
        var export = RunPowerCfg("/export " + Quote(exportPath) + " " + originalGuid);
        if (export.ExitCode != 0 || !File.Exists(exportPath))
            throw new InvalidOperationException("Não foi possível salvar o plano original da sessão automática. A automação não foi iniciada. " + export.Error);

        var session = new PowerProfileSession(originalGuid, exportPath, DateTimeOffset.UtcNow);
        try
        {
            WriteAtomic(SessionPath, JsonSerializer.Serialize(session, JsonOptions));
        }
        catch
        {
            try { File.Delete(exportPath); } catch { }
            throw;
        }
        AppLog.Write("Plano de energia da sessão automática salvo: " + originalGuid);
        return session;
    }

    public static string RestoreAutomaticSession()
    {
        if (!File.Exists(SessionPath))
        {
            TryClearExecutionRequirement();
            return "Não existe uma sessão automática pendente de restauração.";
        }

        var session = JsonSerializer.Deserialize<PowerProfileSession>(File.ReadAllText(SessionPath), JsonOptions)
            ?? throw new InvalidDataException("Metadados da sessão automática estão inválidos.");
        if (!GuidPattern.IsMatch(session.OriginalGuid))
            throw new InvalidDataException("GUID do plano de energia da sessão automática inválido.");

        var exportPath = ValidateSessionExportPath(session.ExportPath);
        var active = "";
        try { active = GetActiveSchemeGuid(); } catch { }

        if (!string.Equals(active, session.OriginalGuid, StringComparison.OrdinalIgnoreCase))
        {
            var activate = RunPowerCfg("/setactive " + session.OriginalGuid);
            if (activate.ExitCode == 0)
            {
                try { active = GetActiveSchemeGuid(); } catch { active = ""; }
            }

            if (!string.Equals(active, session.OriginalGuid, StringComparison.OrdinalIgnoreCase))
            {
                if (!File.Exists(exportPath))
                    throw new InvalidOperationException("O plano original não está disponível e o arquivo exportado da sessão não foi encontrado. O backup foi mantido.");
                var import = RunPowerCfg("/import " + Quote(exportPath) + " " + session.OriginalGuid);
                if (import.ExitCode != 0)
                    throw new InvalidOperationException("Não foi possível importar o plano original da sessão: " + import.Error);
                activate = RunPowerCfg("/setactive " + session.OriginalGuid);
                if (activate.ExitCode != 0)
                    throw new InvalidOperationException("O plano original foi importado, mas não pôde ser ativado: " + activate.Error);
                active = GetActiveSchemeGuid();
            }
        }

        if (!string.Equals(active, session.OriginalGuid, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A restauração automática não foi confirmada. O backup foi mantido.");

        TryClearExecutionRequirement();
        File.Delete(SessionPath);
        if (File.Exists(exportPath)) File.Delete(exportPath);
        AppLog.Write("Plano de energia anterior à sessão automática restaurado: " + active);
        return "Plano de energia anterior à automação restaurado e verificado.";
    }

    public static void RecoverInterruptedAutomaticSession()
    {
        if (!File.Exists(SessionPath)) return;
        var message = RestoreAutomaticSession();
        AppLog.Write("Recuperação de sessão automática interrompida: " + message);
    }

    public static string RestoreOriginal()
    {
        if (!File.Exists(BaseBackupPath))
        {
            TryClearExecutionRequirement();
            return "Não existe um plano original salvo pelo gerenciador de perfis.";
        }

        var backup = JsonSerializer.Deserialize<PowerProfileBackup>(File.ReadAllText(BaseBackupPath), JsonOptions)
            ?? throw new InvalidDataException("Backup do plano de energia inválido.");
        if (!GuidPattern.IsMatch(backup.OriginalGuid))
            throw new InvalidDataException("GUID do plano original inválido.");

        var result = RunPowerCfg("/setactive " + backup.OriginalGuid);
        var active = "";
        if (result.ExitCode == 0)
        {
            try { active = GetActiveSchemeGuid(); } catch { active = ""; }
        }

        if (!string.Equals(active, backup.OriginalGuid, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(backup.ExportPath) && File.Exists(backup.ExportPath))
        {
            var exportPath = Path.GetFullPath(backup.ExportPath);
            var backupsRoot = Path.GetFullPath(AppPaths.Backups) + Path.DirectorySeparatorChar;
            if (!exportPath.StartsWith(backupsRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Caminho do backup do plano original fora da pasta Backups.");
            var import = RunPowerCfg("/import " + Quote(exportPath) + " " + backup.OriginalGuid);
            if (import.ExitCode != 0)
                throw new InvalidOperationException("O plano original não existe e o backup exportado não pôde ser importado: " + import.Error);
            result = RunPowerCfg("/setactive " + backup.OriginalGuid);
            if (result.ExitCode == 0) active = GetActiveSchemeGuid();
        }

        if (result.ExitCode != 0 || !string.Equals(active, backup.OriginalGuid, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Falha ao restaurar/verificar o plano original: " + result.Error);

        TryClearExecutionRequirement();
        File.Delete(BaseBackupPath);
        if (!string.IsNullOrWhiteSpace(backup.ExportPath))
        {
            try
            {
                var export = Path.GetFullPath(backup.ExportPath);
                var root = Path.GetFullPath(AppPaths.Backups) + Path.DirectorySeparatorChar;
                if (export.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(export)) File.Delete(export);
            }
            catch (Exception ex) { AppLog.Write("Não foi possível remover o arquivo .pow após restauração: " + ex.Message); }
        }
        if (File.Exists(StatePath)) File.Delete(StatePath);
        AppLog.Write("Plano de energia original restaurado: " + active);
        return "Plano de energia original restaurado e verificado.";
    }

    public static string GetTargetGuid(PowerProfileMode mode) => ResolveTarget(mode).Guid;

    public static PowerProfileResolution ResolveTarget(PowerProfileMode mode) =>
        ResolveTarget(mode, QueryPowerSchemeGuid);

    public static PowerProfileResolution ResolveTarget(PowerProfileMode mode, Func<string, string?> queryAlias)
    {
        ArgumentNullException.ThrowIfNull(queryAlias);
        var requestedAlias = mode switch
        {
            PowerProfileMode.Performance => "SCHEME_MIN",
            PowerProfileMode.Balanced => "SCHEME_BALANCED",
            PowerProfileMode.Afk => "SCHEME_MAX",
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        var requestedGuid = NormalizeGuid(queryAlias(requestedAlias));
        if (requestedGuid is not null)
            return new PowerProfileResolution(mode, mode, requestedAlias, requestedAlias, requestedGuid);

        if (mode != PowerProfileMode.Balanced)
        {
            var balancedGuid = NormalizeGuid(queryAlias("SCHEME_BALANCED"));
            if (balancedGuid is not null)
                return new PowerProfileResolution(mode, PowerProfileMode.Balanced, requestedAlias, "SCHEME_BALANCED", balancedGuid);
        }

        throw new InvalidOperationException("O Windows não disponibilizou um plano de energia compatível. O ynoez não alterou o plano ativo.");
    }

    private static string? QueryPowerSchemeGuid(string alias)
    {
        var result = RunPowerCfg("/query " + alias);
        if (result.ExitCode != 0) return null;
        var match = Regex.Match(result.Output, @"[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}");
        return match.Success ? NormalizeGuid(match.Value) : null;
    }

    private static string? NormalizeGuid(string? value) =>
        Guid.TryParse(value, out var parsed) ? parsed.ToString() : null;

    public static void ClearExecutionRequirement() => TryClearExecutionRequirement();

    private static void SetExecutionRequirement(PowerProfileMode mode)
    {
        if (mode != PowerProfileMode.Afk)
        {
            TryClearExecutionRequirement();
            return;
        }

        lock (PowerRequestSync)
        {
            if (_systemSleepRequest == IntPtr.Zero)
            {
                var reason = Marshal.StringToHGlobalUni("ynoez AFK: manter jogos, scripts e downloads ativos");
                try
                {
                    var context = new PowerRequestContext
                    {
                        Version = 0,
                        Flags = PowerRequestContextSimpleString,
                        SimpleReasonString = reason
                    };
                    _systemSleepRequest = PowerCreateRequest(ref context);
                }
                finally { Marshal.FreeHGlobal(reason); }

                if (_systemSleepRequest == IntPtr.Zero || _systemSleepRequest == new IntPtr(-1))
                {
                    _systemSleepRequest = IntPtr.Zero;
                    throw new Win32Exception(Marshal.GetLastWin32Error(),
                        "O Windows não permitiu criar uma solicitação persistente para manter o sistema ativo.");
                }
            }

            if (!PowerSetRequest(_systemSleepRequest, PowerRequestType.SystemRequired))
            {
                var error = Marshal.GetLastWin32Error();
                CloseHandle(_systemSleepRequest);
                _systemSleepRequest = IntPtr.Zero;
                throw new Win32Exception(error, "O Windows não permitiu bloquear a suspensão automática durante o perfil AFK.");
            }
        }
    }

    private static void TryClearExecutionRequirement()
    {
        lock (PowerRequestSync)
        {
            if (_systemSleepRequest == IntPtr.Zero) return;
            var handle = _systemSleepRequest;
            _systemSleepRequest = IntPtr.Zero;

            if (!PowerClearRequest(handle, PowerRequestType.SystemRequired))
                AppLog.Write("O Windows não confirmou a liberação explícita da solicitação AFK: " +
                    new Win32Exception(Marshal.GetLastWin32Error()).Message);

            if (!CloseHandle(handle))
                AppLog.Write("O Windows não confirmou o fechamento do identificador de energia: " +
                    new Win32Exception(Marshal.GetLastWin32Error()).Message);
        }
    }

    private static string ValidateSessionExportPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("Caminho de exportação da sessão vazio.");
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetFullPath(AppPaths.Backups) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetExtension(fullPath), ".pow", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Caminho da exportação da sessão fora da pasta Backups.");
        return fullPath;
    }

    private static void TrySetActive(string guid)
    {
        try { RunPowerCfg("/setactive " + guid); } catch { }
    }

    private static (int ExitCode, string Output, string Error) RunPowerCfg(string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("powercfg.exe", arguments)
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
        process.WaitForExit();
        return (process.ExitCode, outputTask.GetAwaiter().GetResult(), errorTask.GetAwaiter().GetResult());
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static void WriteAtomic(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, overwrite: true);
    }
}
