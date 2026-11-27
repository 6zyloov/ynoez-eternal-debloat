using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ynoez.Services;

public sealed record AutoProfileStatus(
    PowerProfileMode Mode,
    PowerProfileMode AppliedMode,
    bool UsedFallback,
    bool GameDetected,
    TimeSpan IdleTime,
    string ActiveGame,
    string ActiveProtectedProcess);

public static class AutoProfileService
{
    private static readonly string GamesPath = Path.Combine(AppContext.BaseDirectory, "Optimization", "games.json");
    private static readonly string UserSettingsPath = Path.Combine(AppPaths.Data, "auto-profile-settings.json");
    private static readonly string[] DefaultGames =
    {
        "RobloxPlayerBeta", "VALORANT-Win64-Shipping", "cs2", "FortniteClient-Win64-Shipping",
        "r5apex", "GTA5", "League of Legends", "Overwatch", "Minecraft", "Minecraft.Windows"
    };
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private sealed record AutoProfileSettings(
        string[] GameProcessNames,
        string[] ProtectedProcessNames,
        int IdleThresholdMinutes,
        int PollIntervalSeconds,
        bool ProtectionConfigInvalid);

    private sealed record UserSettings(int SchemaVersion, string[] ProtectedProcessNames);

    public static IReadOnlyList<string> GetProtectedProcessNames() =>
        LoadSettings().ProtectedProcessNames;

    /// <summary>
    /// Saves extra process names that must not enter the aggressive AFK power plan.
    /// Values are process image names, not paths or command lines.
    /// </summary>
    public static IReadOnlyList<string> SaveProtectedProcessNames(IEnumerable<string> processNames)
    {
        ArgumentNullException.ThrowIfNull(processNames);
        var normalized = NormalizeProtectedProcessNames(processNames);
        Directory.CreateDirectory(AppPaths.Data);
        var path = UserSettingsPath;
        var tempPath = path + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(new UserSettings(1, normalized), JsonOptions);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, path, overwrite: true);
            AppLog.Write("Lista de processos protegidos do perfil automático salva: " +
                (normalized.Length == 0 ? "(vazia)" : string.Join(", ", normalized)));
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
        return normalized;
    }

    internal static string[] NormalizeProtectedProcessNames(IEnumerable<string> processNames)
    {
        ArgumentNullException.ThrowIfNull(processNames);
        var result = new List<string>();
        foreach (var raw in processNames)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var value = raw.Trim();
            if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                value = value[..^4].Trim();

            if (value.Length == 0) continue;
            if (value.Length > 128 ||
                value.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)) ||
                value.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|', ';' }) >= 0)
                throw new ArgumentException(
                    "Nome de processo inválido. Informe somente o nome do executável, sem caminho, argumentos ou curingas: " + raw,
                    nameof(processNames));

            if (!result.Contains(value, StringComparer.OrdinalIgnoreCase))
                result.Add(value);
            if (result.Count > 64)
                throw new ArgumentException("É permitido cadastrar no máximo 64 processos protegidos.", nameof(processNames));
        }
        return result.ToArray();
    }

    internal static PowerProfileMode DecideMode(
        TimeSpan idleTime,
        TimeSpan idleThreshold,
        bool gameRunning,
        bool protectedProcessRunning,
        bool protectionConfigInvalid = false)
    {
        if (idleThreshold < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(idleThreshold));
        if (idleTime < TimeSpan.Zero)
            idleTime = TimeSpan.Zero;

        // Recent user input during a detected game keeps the normal gaming profile.
        // When the user is AFK, a game or explicitly protected task uses Balanced +
        // a sleep-prevention request, never the aggressive power-saver plan.
        if (gameRunning && idleTime < idleThreshold)
            return PowerProfileMode.Performance;
        if (protectionConfigInvalid)
            return PowerProfileMode.ProtectedAfk;
        if (gameRunning || protectedProcessRunning)
            return PowerProfileMode.ProtectedAfk;

        return idleTime >= idleThreshold
            ? PowerProfileMode.Afk
            : PowerProfileMode.Balanced;
    }

    public static string GetModeDisplayName(PowerProfileMode mode) => mode switch
    {
        PowerProfileMode.Performance => "Performance",
        PowerProfileMode.Balanced => "Equilibrado",
        PowerProfileMode.Afk => "AFK / Economia",
        PowerProfileMode.ProtectedAfk => "AFK protegido (Equilibrado, sem suspensão)",
        _ => mode.ToString()
    };

    public static async Task RunAsync(Action<AutoProfileStatus>? onStatus, CancellationToken token)
    {
        _ = PowerProfileService.CaptureAutomaticSession();
        try
        {
            var settings = LoadSettings();
            var idleThreshold = TimeSpan.FromMinutes(settings.IdleThresholdMinutes);
            var pollInterval = TimeSpan.FromSeconds(settings.PollIntervalSeconds);
            PowerProfileMode? lastMode = null;
            string? lastGame = null;
            string? lastProtectedProcess = null;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var idle = GetIdleTime();
                var game = FindRunningProcess(settings.GameProcessNames);
                var protectedProcess = settings.ProtectionConfigInvalid
                    ? "Configuração de proteção inválida — economia agressiva bloqueada"
                    : FindRunningProcess(settings.ProtectedProcessNames);
                var desired = DecideMode(idle, idleThreshold, game.Length > 0,
                    protectedProcess.Length > 0, settings.ProtectionConfigInvalid);

                var resolution = PowerProfileService.ResolveTarget(desired);
                var active = PowerProfileService.GetActiveSchemeGuid();
                if (!string.Equals(active, resolution.Guid, StringComparison.OrdinalIgnoreCase)
                    || lastMode != desired)
                    _ = PowerProfileService.ApplyForAutomaticSession(desired);

                if (lastMode != desired ||
                    !string.Equals(lastGame, game, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(lastProtectedProcess, protectedProcess, StringComparison.OrdinalIgnoreCase))
                {
                    onStatus?.Invoke(new AutoProfileStatus(
                        desired,
                        resolution.AppliedMode,
                        resolution.UsedFallback,
                        game.Length > 0,
                        idle,
                        game,
                        protectedProcess));
                    lastMode = desired;
                    lastGame = game;
                    lastProtectedProcess = protectedProcess;
                }
                await Task.Delay(pollInterval, token);
            }
        }
        finally
        {
            try
            {
                var message = PowerProfileService.RestoreAutomaticSession();
                AppLog.Write("Sessão automática finalizada: " + message);
            }
            catch (Exception ex)
            {
                AppLog.Write("Falha ao restaurar plano original após sessão automática. O backup foi preservado para recuperação na próxima abertura: " + ex);
                throw;
            }
        }
    }

    private static AutoProfileSettings LoadSettings()
    {
        var gameNames = DefaultGames;
        var idleMinutes = 15;
        var pollSeconds = 15;
        var shippedProtected = Array.Empty<string>();
        var protectionConfigInvalid = false;

        try
        {
            if (File.Exists(GamesPath))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(GamesPath));
                var root = document.RootElement;
                if (root.TryGetProperty("processNames", out var gamesElement) &&
                    gamesElement.ValueKind == JsonValueKind.Array)
                {
                    var names = gamesElement.EnumerateArray()
                        .Where(x => x.ValueKind == JsonValueKind.String)
                        .Select(x => x.GetString())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x!)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    if (names.Length > 0) gameNames = names;
                }

                if (root.TryGetProperty("protectedProcessNames", out var protectedElement) &&
                    protectedElement.ValueKind == JsonValueKind.Array)
                {
                    shippedProtected = NormalizeProtectedProcessNames(
                        protectedElement.EnumerateArray()
                            .Where(x => x.ValueKind == JsonValueKind.String)
                            .Select(x => x.GetString() ?? ""));
                }

                if (root.TryGetProperty("idleThresholdMinutes", out var idleElement) &&
                    idleElement.TryGetInt32(out var idle))
                    idleMinutes = Math.Clamp(idle, 1, 120);
                if (root.TryGetProperty("pollIntervalSeconds", out var pollElement) &&
                    pollElement.TryGetInt32(out var poll))
                    pollSeconds = Math.Clamp(poll, 5, 120);
            }
        }
        catch (Exception ex)
        {
            protectionConfigInvalid = true;
            AppLog.Write("Configuração de jogos inválida; economia agressiva será bloqueada por segurança: " + ex.Message);
        }

        var protectedNames = shippedProtected;
        try
        {
            if (File.Exists(UserSettingsPath))
            {
                var userSettings = ParseUserSettings(File.ReadAllText(UserSettingsPath));
                protectedNames = NormalizeProtectedProcessNames(userSettings);
            }
        }
        catch (Exception ex)
        {
            // Do not silently discard a corrupted user configuration. Preserve the file
            // and prevent the aggressive AFK plan until the user repairs the configuration.
            protectionConfigInvalid = true;
            AppLog.Write("Lista de processos protegidos inválida; economia agressiva será bloqueada por segurança: " + ex.Message);
        }

        return new AutoProfileSettings(gameNames, protectedNames, idleMinutes, pollSeconds, protectionConfigInvalid);
    }

    internal static string[] ParseUserSettings(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("Configuração de processos protegidos vazia.");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Configuração de processos protegidos inválida.");
        if (!root.TryGetProperty("protectedProcessNames", out var processNames) ||
            processNames.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("A lista protectedProcessNames não existe ou não é um array.");
        var values = processNames.EnumerateArray()
            .Select(x => x.ValueKind == JsonValueKind.String
                ? x.GetString() ?? ""
                : throw new InvalidDataException("Todos os processos protegidos precisam ser textos."));
        return NormalizeProtectedProcessNames(values);
    }

    private static string FindRunningProcess(IEnumerable<string> processNames)
    {
        foreach (var name in processNames)
        {
            Process[] processes = Array.Empty<Process>();
            try
            {
                processes = Process.GetProcessesByName(name);
                if (processes.Length > 0) return name;
            }
            catch (Exception ex)
            {
                AppLog.Write("Não foi possível consultar o processo " + name + ": " + ex.Message);
            }
            finally
            {
                foreach (var process in processes) process.Dispose();
            }
        }
        return string.Empty;
    }

    private static TimeSpan GetIdleTime()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
        var elapsed = unchecked((uint)Environment.TickCount - info.Time);
        return TimeSpan.FromMilliseconds(elapsed);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo plii);
}
