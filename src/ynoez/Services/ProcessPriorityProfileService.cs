using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace ynoez.Services;

public sealed record ProcessPriorityProfileSettings
{
    public bool Enabled { get; init; }
    public string? FocusExecutablePath { get; init; }
    public List<string> BackgroundExecutablePaths { get; init; } = [];
    public bool UseHighCpuPriority { get; init; } = true;
    public bool UseLowestBackgroundCpuPriority { get; init; } = true;
}

public sealed record ProcessPriorityApplySummary(
    int FocusProcesses,
    int BackgroundProcesses,
    int ChangedProcesses,
    int SkippedProcesses,
    int GpuPreferencesUpdated,
    IReadOnlyList<string> Warnings)
{
    public string ToDisplayText()
    {
        var state = $"Foco: {FocusProcesses} processo(s) | Segundo plano: {BackgroundProcesses} processo(s) | Alterados: {ChangedProcesses}";
        if (GpuPreferencesUpdated > 0)
            state += $" | Preferências de GPU: {GpuPreferencesUpdated}";
        if (SkippedProcesses > 0)
            state += $" | Sem acesso: {SkippedProcesses}";
        if (Warnings.Count > 0)
            state += Environment.NewLine + string.Join(Environment.NewLine, Warnings.Take(3));
        return state;
    }
}

/// <summary>
/// Applies reversible per-process CPU and memory priorities and Windows per-app GPU adapter preferences.
/// It never changes thread priorities, CPU affinity, process working-set limits, or GPU scheduler internals.
/// </summary>
public static class ProcessPriorityProfileService
{
    private const uint ProcessSetInformation = 0x0200;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessQueryLimitedInformation = 0x1000;

    private const uint IdlePriorityClass = 0x00000040;
    private const uint AboveNormalPriorityClass = 0x00008000;
    private const uint HighPriorityClass = 0x00000080;

    private const int ProcessMemoryPriorityInformation = 0;
    private const uint MemoryPriorityVeryLow = 1;
    private const uint MemoryPriorityNormal = 5;

    private const string GpuPreferencesSubKey = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private const string HighPerformanceGpuPreference = "GpuPreference=2;";
    private const string PowerSavingGpuPreference = "GpuPreference=1;";

    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static string? _journalLoadError;
    private static readonly HashSet<string> ProtectedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Registry", "Idle", "smss", "csrss", "wininit", "winlogon",
        "services", "lsass", "svchost", "fontdrvhost", "dwm", "Memory Compression",
        "ynoez"
    };

    private static string ProfilePath => Path.Combine(AppPaths.Data, "process-priority-profile.json");
    private static string JournalPath => Path.Combine(AppPaths.Data, "process-priority-session.json");

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryPriorityInformation
    {
        public uint MemoryPriority;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;

        public readonly long ToInt64() => ((long)HighDateTime << 32) | LowDateTime;
    }

    private sealed class SessionJournal
    {
        public SessionJournal() { }
        public List<ProcessStateSnapshot> Processes { get; set; } = [];
        public List<GpuPreferenceSnapshot> GpuPreferences { get; set; } = [];
    }

    private sealed class ProcessStateSnapshot
    {
        public ProcessStateSnapshot() { }
        public int ProcessId { get; set; }
        public long CreationTimeFileTimeUtc { get; set; }
        public uint OriginalPriorityClass { get; set; }
        public uint AppliedPriorityClass { get; set; }
        public uint OriginalMemoryPriority { get; set; }
        public uint AppliedMemoryPriority { get; set; }
        public bool HasMemoryPriority { get; set; }
    }

    private sealed class GpuPreferenceSnapshot
    {
        public GpuPreferenceSnapshot() { }
        public string ExecutablePath { get; set; } = string.Empty;
        public bool OriginalValueExisted { get; set; }
        public string? OriginalValue { get; set; }
        public int OriginalValueKind { get; set; }
        public string AppliedValue { get; set; } = string.Empty;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(
        SafeProcessHandle process,
        out NativeFileTime creationTime,
        out NativeFileTime exitTime,
        out NativeFileTime kernelTime,
        out NativeFileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetPriorityClass(SafeProcessHandle process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetPriorityClass(SafeProcessHandle process, uint priorityClass);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "GetProcessInformation")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessInformationMemory(
        SafeProcessHandle process,
        int informationClass,
        out MemoryPriorityInformation information,
        uint informationSize);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "SetProcessInformation")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessInformationMemory(
        SafeProcessHandle process,
        int informationClass,
        in MemoryPriorityInformation information,
        uint informationSize);

    public static ProcessPriorityProfileSettings LoadProfile()
    {
        lock (Sync)
        {
            try
            {
                if (!File.Exists(ProfilePath))
                    return new ProcessPriorityProfileSettings();

                var loaded = JsonSerializer.Deserialize<ProcessPriorityProfileSettings>(File.ReadAllText(ProfilePath), JsonOptions);
                return NormalizeSettings(loaded ?? new ProcessPriorityProfileSettings());
            }
            catch (Exception ex)
            {
                AppLog.Write("Não foi possível carregar o perfil de prioridade por aplicativo: " + ex.Message);
                return new ProcessPriorityProfileSettings();
            }
        }
    }

    public static void SaveProfile(ProcessPriorityProfileSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (Sync)
            SaveProfileCore(NormalizeSettings(settings));
    }

    public static ProcessPriorityApplySummary StartSession(ProcessPriorityProfileSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (Sync)
        {
            var normalized = NormalizeSettings(settings with { Enabled = true });
            var restore = RestoreJournalCore();
            if (restore.Warnings.Count > 0)
            {
                AppLog.Write("O novo perfil de prioridades não foi iniciado porque a sessão anterior ainda tem itens que não puderam ser restaurados.");
                return restore;
            }

            SaveProfileCore(normalized);
            var journal = new SessionJournal();
            var gpuCount = ApplyGpuPreferences(normalized, journal);
            SaveJournalCore(journal);

            var result = ApplyCycleCore(normalized, journal) with { GpuPreferencesUpdated = gpuCount };
            AppLog.Write("Perfil de prioridade por aplicativo iniciado. " + result.ToDisplayText());
            return result;
        }
    }

    public static ProcessPriorityApplySummary ApplyCycle()
    {
        lock (Sync)
        {
            var settings = LoadProfileCore();
            if (!settings.Enabled)
                return EmptySummary();

            var journal = LoadJournalCore();
            if (journal is null)
            {
                if (_journalLoadError is not null)
                    return new ProcessPriorityApplySummary(0, 0, 0, 0, 0, [_journalLoadError]);

                journal = new SessionJournal();
                var gpuCount = ApplyGpuPreferences(settings, journal);
                SaveJournalCore(journal);
                return ApplyCycleCore(settings, journal) with { GpuPreferencesUpdated = gpuCount };
            }

            return ApplyCycleCore(settings, journal);
        }
    }

    public static ProcessPriorityApplySummary StopSession()
    {
        lock (Sync)
        {
            var settings = LoadProfileCore() with { Enabled = false };
            SaveProfileCore(settings);
            var result = RestoreJournalCore();
            AppLog.Write("Perfil de prioridade por aplicativo desativado. " + result.ToDisplayText());
            return result;
        }
    }

    /// <summary>
    /// Restores a journal left by a previous process lifetime. Called before resuming a saved profile
    /// and again during orderly shutdown. PID creation times prevent restoring settings onto reused PIDs.
    /// </summary>
    public static ProcessPriorityApplySummary RecoverInterruptedSession()
    {
        lock (Sync)
        {
            var result = RestoreJournalCore();
            if (result.Warnings.Count > 0)
                AppLog.Write("A recuperação da sessão de prioridade por aplicativo ficou incompleta. " + result.ToDisplayText());
            else if (result.ChangedProcesses > 0 || result.GpuPreferencesUpdated > 0)
                AppLog.Write("Sessão de prioridade por aplicativo restaurada.");
            return result;
        }
    }

    public static ProcessPriorityProfileSettings NormalizeSettings(ProcessPriorityProfileSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var focusPath = NormalizeExecutablePath(settings.FocusExecutablePath);
        var focusName = GetExecutableName(focusPath);
        var background = (settings.BackgroundExecutablePaths ?? [])
            .Select(NormalizeExecutablePath)
            .Where(path => path is not null)
            .Select(path => path!)
            .Where(path => !string.Equals(GetExecutableName(path), focusName, StringComparison.OrdinalIgnoreCase))
            .GroupBy(GetExecutableName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();

        return settings with
        {
            FocusExecutablePath = focusPath,
            BackgroundExecutablePaths = background
        };
    }

    public static bool MatchesExecutableName(string? processName, string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(processName) || string.IsNullOrWhiteSpace(executablePath))
            return false;

        return string.Equals(
            Path.GetFileNameWithoutExtension(processName.Trim()),
            GetExecutableName(executablePath),
            StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsProtectedProcessName(string? processName) =>
        !string.IsNullOrWhiteSpace(processName) && ProtectedProcessNames.Contains(Path.GetFileNameWithoutExtension(processName.Trim()));

    private static ProcessPriorityApplySummary ApplyCycleCore(ProcessPriorityProfileSettings settings, SessionJournal journal)
    {
        var focusName = GetExecutableName(settings.FocusExecutablePath);
        var backgroundNames = new HashSet<string>(
            settings.BackgroundExecutablePaths.Select(GetExecutableName).Where(x => x.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        var focusCount = 0;
        var backgroundCount = 0;
        var changedCount = 0;
        var skippedCount = 0;
        var warnings = new List<string>();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var processName = process.ProcessName;
                    if (IsProtectedProcessName(processName))
                        continue;

                    var isFocus = focusName.Length > 0 && MatchesExecutableName(processName, settings.FocusExecutablePath);
                    var isBackground = !isFocus && backgroundNames.Contains(Path.GetFileNameWithoutExtension(processName));
                    if (!isFocus && !isBackground)
                        continue;

                    if (isFocus) focusCount++;
                    else backgroundCount++;

                    var accessible = TryApplyProcess(process, isFocus, settings.UseHighCpuPriority, settings.UseLowestBackgroundCpuPriority, journal, out var changed, out var warning);
                    if (changed) changedCount++;
                    if (!accessible || !string.IsNullOrWhiteSpace(warning))
                    {
                        skippedCount++;
                        if (!string.IsNullOrWhiteSpace(warning) && warnings.Count < 6)
                            warnings.Add(warning);
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    skippedCount++;
                    if (warnings.Count < 6)
                        warnings.Add($"Um processo não pôde ser consultado: {ex.Message}");
                }
            }
        }

        if (changedCount > 0)
            SaveJournalCore(journal);

        return new ProcessPriorityApplySummary(focusCount, backgroundCount, changedCount, skippedCount, journal.GpuPreferences.Count, warnings);
    }

    private static bool TryApplyProcess(
        Process process,
        bool isFocus,
        bool useHighCpuPriority,
        bool useLowestBackgroundCpuPriority,
        SessionJournal journal,
        out bool changed,
        out string warning)
    {
        changed = false;
        warning = string.Empty;

        var access = ProcessSetInformation | ProcessQueryInformation | ProcessQueryLimitedInformation;
        using var handle = OpenProcess(access, false, process.Id);
        if (handle.IsInvalid)
        {
            warning = $"Sem acesso para ajustar {process.ProcessName} (PID {process.Id}); tente abrir o ynoez como administrador.";
            return false;
        }

        if (!GetProcessTimes(handle, out var creationTime, out _, out _, out _))
        {
            warning = $"Não foi possível validar o PID {process.Id}; ele pode ter sido encerrado.";
            return false;
        }

        var creationFileTime = creationTime.ToInt64();
        var state = journal.Processes.FirstOrDefault(x =>
            x.ProcessId == process.Id && x.CreationTimeFileTimeUtc == creationFileTime);

        if (state is null)
        {
            var originalPriority = GetPriorityClass(handle);
            if (originalPriority == 0)
            {
                warning = $"Não foi possível ler a prioridade original de {process.ProcessName} (PID {process.Id}).";
                return false;
            }

            var memoryAvailable = GetProcessInformationMemory(
                handle, ProcessMemoryPriorityInformation, out var memoryInformation,
                (uint)Marshal.SizeOf<MemoryPriorityInformation>());

            state = new ProcessStateSnapshot
            {
                ProcessId = process.Id,
                CreationTimeFileTimeUtc = creationFileTime,
                OriginalPriorityClass = originalPriority,
                AppliedPriorityClass = originalPriority,
                OriginalMemoryPriority = memoryAvailable ? memoryInformation.MemoryPriority : 0,
                AppliedMemoryPriority = memoryAvailable ? memoryInformation.MemoryPriority : 0,
                HasMemoryPriority = memoryAvailable
            };
            journal.Processes.RemoveAll(x => x.ProcessId == process.Id);
            journal.Processes.Add(state);
            SaveJournalCore(journal);
        }

        var desiredCpuPriority = isFocus
            ? (useHighCpuPriority ? HighPriorityClass : AboveNormalPriorityClass)
            : (useLowestBackgroundCpuPriority ? IdlePriorityClass : 0x00004000u);
        var desiredMemoryPriority = isFocus ? MemoryPriorityNormal : MemoryPriorityVeryLow;

        var currentCpuPriority = GetPriorityClass(handle);
        if (currentCpuPriority == 0)
        {
            warning = $"Não foi possível ler a prioridade de CPU de {process.ProcessName} (PID {process.Id}): {Marshal.GetLastWin32Error()}.";
        }
        else if (currentCpuPriority != desiredCpuPriority)
        {
            // Persist the intended value before the native call so crash recovery knows
            // what to restore even if the app exits immediately after SetPriorityClass.
            state.AppliedPriorityClass = desiredCpuPriority;
            SaveJournalCore(journal);
            if (SetPriorityClass(handle, desiredCpuPriority))
            {
                changed = true;
            }
            else
            {
                state.AppliedPriorityClass = currentCpuPriority;
                SaveJournalCore(journal);
                warning = $"O Windows negou a alteração de CPU de {process.ProcessName} (PID {process.Id}): {Marshal.GetLastWin32Error()}.";
            }
        }

        if (state.HasMemoryPriority
            && GetProcessInformationMemory(handle, ProcessMemoryPriorityInformation, out var currentMemory, (uint)Marshal.SizeOf<MemoryPriorityInformation>()))
        {
            if (currentMemory.MemoryPriority != desiredMemoryPriority)
            {
                var desired = new MemoryPriorityInformation { MemoryPriority = desiredMemoryPriority };
                // As above, journal the requested value before changing the process.
                state.AppliedMemoryPriority = desiredMemoryPriority;
                SaveJournalCore(journal);
                if (SetProcessInformationMemory(handle, ProcessMemoryPriorityInformation, in desired, (uint)Marshal.SizeOf<MemoryPriorityInformation>()))
                {
                    changed = true;
                }
                else
                {
                    state.AppliedMemoryPriority = currentMemory.MemoryPriority;
                    SaveJournalCore(journal);
                    if (string.IsNullOrWhiteSpace(warning))
                        warning = $"O Windows negou a alteração de prioridade de memória de {process.ProcessName} (PID {process.Id}): {Marshal.GetLastWin32Error()}.";
                }
            }
        }

        if (changed)
            SaveJournalCore(journal);

        return true;
    }

    private static int ApplyGpuPreferences(ProcessPriorityProfileSettings settings, SessionJournal journal)
    {
        var updated = 0;
        if (!string.IsNullOrWhiteSpace(settings.FocusExecutablePath)
            && WriteGpuPreference(settings.FocusExecutablePath, HighPerformanceGpuPreference, journal))
            updated++;

        foreach (var path in settings.BackgroundExecutablePaths)
            if (WriteGpuPreference(path, PowerSavingGpuPreference, journal))
                updated++;

        return updated;
    }

    private static bool WriteGpuPreference(string executablePath, string preference, SessionJournal journal)
    {
        try
        {
            var normalizedPath = Path.GetFullPath(executablePath);
            var snapshot = journal.GpuPreferences.FirstOrDefault(
                x => string.Equals(x.ExecutablePath, normalizedPath, StringComparison.OrdinalIgnoreCase));

            using var key = Registry.CurrentUser.CreateSubKey(GpuPreferencesSubKey, writable: true);
            if (key is null)
            {
                AppLog.Write("Não foi possível abrir a configuração de GPU do Windows.");
                return false;
            }

            if (snapshot is null)
            {
                var original = key.GetValue(normalizedPath, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                var existed = original is not null;
                var kind = existed ? key.GetValueKind(normalizedPath) : RegistryValueKind.String;
                snapshot = new GpuPreferenceSnapshot
                {
                    ExecutablePath = normalizedPath,
                    OriginalValueExisted = existed,
                    OriginalValue = original?.ToString(),
                    OriginalValueKind = (int)kind,
                    AppliedValue = preference
                };
                journal.GpuPreferences.Add(snapshot);
            }
            else
            {
                snapshot.AppliedValue = preference;
            }

            // Save the intended value before the registry write; recovery can then
            // compare the current value and restore only if our change actually landed.
            SaveJournalCore(journal);
            key.SetValue(normalizedPath, preference, RegistryValueKind.String);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            AppLog.Write($"Não foi possível definir a preferência de GPU para {executablePath}: {ex.Message}");
            return false;
        }
    }

    private static ProcessPriorityApplySummary RestoreJournalCore()
    {
        var journal = LoadJournalCore();
        if (journal is null)
            return _journalLoadError is null
                ? EmptySummary()
                : new ProcessPriorityApplySummary(0, 0, 0, 1, 0, [_journalLoadError]);

        var warnings = new List<string>();
        var restoredProcesses = 0;
        var remainingProcesses = new List<ProcessStateSnapshot>();

        foreach (var state in journal.Processes)
        {
            using var handle = OpenProcess(
                ProcessSetInformation | ProcessQueryInformation | ProcessQueryLimitedInformation,
                false,
                state.ProcessId);

            if (handle.IsInvalid)
            {
                if (IsProcessStillRunning(state.ProcessId))
                {
                    remainingProcesses.Add(state);
                    warnings.Add($"Sem acesso para restaurar o PID {state.ProcessId}; a restauração ficará pendente.");
                }
                continue;
            }

            if (!GetProcessTimes(handle, out var creationTime, out _, out _, out _))
            {
                remainingProcesses.Add(state);
                warnings.Add($"Não foi possível validar o PID {state.ProcessId}; a restauração ficará pendente.");
                continue;
            }

            if (creationTime.ToInt64() != state.CreationTimeFileTimeUtc)
                continue;

            var restored = false;
            var unresolved = false;
            var currentCpu = GetPriorityClass(handle);
            if (currentCpu == 0 && state.AppliedPriorityClass != state.OriginalPriorityClass)
            {
                unresolved = true;
                warnings.Add($"Não foi possível ler a prioridade de CPU do PID {state.ProcessId}; a restauração ficará pendente.");
            }
            else if (currentCpu == state.AppliedPriorityClass && currentCpu != state.OriginalPriorityClass)
            {
                if (SetPriorityClass(handle, state.OriginalPriorityClass))
                    restored = true;
                else
                {
                    unresolved = true;
                    warnings.Add($"Não foi possível restaurar a prioridade de CPU do PID {state.ProcessId}: {Marshal.GetLastWin32Error()}.");
                }
            }

            if (state.HasMemoryPriority)
            {
                if (!GetProcessInformationMemory(handle, ProcessMemoryPriorityInformation, out var currentMemory, (uint)Marshal.SizeOf<MemoryPriorityInformation>()))
                {
                    if (state.AppliedMemoryPriority != state.OriginalMemoryPriority)
                    {
                        unresolved = true;
                        warnings.Add($"Não foi possível ler a prioridade de memória do PID {state.ProcessId}; a restauração ficará pendente.");
                    }
                }
                else if (currentMemory.MemoryPriority == state.AppliedMemoryPriority
                    && currentMemory.MemoryPriority != state.OriginalMemoryPriority)
                {
                    var original = new MemoryPriorityInformation { MemoryPriority = state.OriginalMemoryPriority };
                    if (SetProcessInformationMemory(handle, ProcessMemoryPriorityInformation, in original, (uint)Marshal.SizeOf<MemoryPriorityInformation>()))
                        restored = true;
                    else
                    {
                        unresolved = true;
                        warnings.Add($"Não foi possível restaurar a prioridade de memória do PID {state.ProcessId}: {Marshal.GetLastWin32Error()}.");
                    }
                }
            }

            if (unresolved)
                remainingProcesses.Add(state);
            else if (restored)
                restoredProcesses++;
        }

        var remainingGpu = new List<GpuPreferenceSnapshot>();
        var restoredGpu = 0;
        foreach (var snapshot in journal.GpuPreferences)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(GpuPreferencesSubKey, writable: true);
                if (key is null)
                {
                    remainingGpu.Add(snapshot);
                    warnings.Add("Não foi possível abrir o registro de preferências de GPU para restaurá-lo.");
                    continue;
                }

                var current = key.GetValue(snapshot.ExecutablePath, null, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString();
                if (!string.Equals(current, snapshot.AppliedValue, StringComparison.Ordinal))
                    continue;

                if (snapshot.OriginalValueExisted)
                {
                    var kind = Enum.IsDefined(typeof(RegistryValueKind), snapshot.OriginalValueKind)
                        ? (RegistryValueKind)snapshot.OriginalValueKind
                        : RegistryValueKind.String;
                    key.SetValue(snapshot.ExecutablePath, snapshot.OriginalValue ?? string.Empty, kind);
                }
                else
                {
                    key.DeleteValue(snapshot.ExecutablePath, throwOnMissingValue: false);
                }

                restoredGpu++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
            {
                remainingGpu.Add(snapshot);
                warnings.Add($"Não foi possível restaurar a preferência de GPU para {Path.GetFileName(snapshot.ExecutablePath)}: {ex.Message}");
            }
        }

        journal.Processes = remainingProcesses;
        journal.GpuPreferences = remainingGpu;
        if (remainingProcesses.Count == 0 && remainingGpu.Count == 0)
        {
            try { File.Delete(JournalPath); } catch { }
        }
        else
        {
            SaveJournalCore(journal);
        }

        return new ProcessPriorityApplySummary(0, 0, restoredProcesses, warnings.Count, restoredGpu, warnings);
    }

    private static ProcessPriorityProfileSettings LoadProfileCore()
    {
        try
        {
            if (!File.Exists(ProfilePath))
                return new ProcessPriorityProfileSettings();
            return NormalizeSettings(JsonSerializer.Deserialize<ProcessPriorityProfileSettings>(File.ReadAllText(ProfilePath), JsonOptions)
                ?? new ProcessPriorityProfileSettings());
        }
        catch (Exception ex)
        {
            AppLog.Write("Falha ao ler o perfil de prioridade por aplicativo: " + ex.Message);
            return new ProcessPriorityProfileSettings();
        }
    }

    private static SessionJournal? LoadJournalCore()
    {
        try
        {
            if (!File.Exists(JournalPath))
            {
                _journalLoadError = null;
                return null;
            }

            var loaded = JsonSerializer.Deserialize<SessionJournal>(File.ReadAllText(JournalPath), JsonOptions);
            if (loaded is null)
                throw new InvalidDataException("O diário JSON retornou um objeto vazio.");

            _journalLoadError = null;
            return loaded;
        }
        catch (Exception ex)
        {
            _journalLoadError = "O diário de prioridade está inválido e foi preservado. Não será aplicada outra sessão até a restauração ser resolvida: " + ex.Message;
            AppLog.Write(_journalLoadError);
            return null;
        }
    }

    private static bool IsProcessStillRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return true;
        }
    }

    private static void SaveProfileCore(ProcessPriorityProfileSettings settings)
    {
        Directory.CreateDirectory(AppPaths.Data);
        WriteJsonAtomically(ProfilePath, settings);
    }

    private static void SaveJournalCore(SessionJournal journal)
    {
        Directory.CreateDirectory(AppPaths.Data);
        WriteJsonAtomically(JournalPath, journal);
    }

    private static void WriteJsonAtomically<T>(string path, T value)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(temporary, path, overwrite: true);
    }

    private static string? NormalizeExecutablePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            var normalized = Path.GetFullPath(path.Trim());
            return string.Equals(Path.GetExtension(normalized), ".exe", StringComparison.OrdinalIgnoreCase)
                ? normalized
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string GetExecutableName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;
        return Path.GetFileNameWithoutExtension(path.Trim());
    }

    private static ProcessPriorityApplySummary EmptySummary() =>
        new(0, 0, 0, 0, 0, Array.Empty<string>());
}
