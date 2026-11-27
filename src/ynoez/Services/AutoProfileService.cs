using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ynoez.Services;

public sealed record AutoProfileStatus(PowerProfileMode Mode, bool GameDetected, TimeSpan IdleTime, string ActiveGame);

public static class AutoProfileService
{
    private static readonly string GamesPath = Path.Combine(AppContext.BaseDirectory, "Optimization", "games.json");
    private static readonly string[] DefaultGames =
    {
        "RobloxPlayerBeta", "VALORANT-Win64-Shipping", "cs2", "FortniteClient-Win64-Shipping",
        "r5apex", "GTA5", "League of Legends", "Overwatch", "Minecraft", "javaw"
    };

    public static async Task RunAsync(Action<AutoProfileStatus>? onStatus, CancellationToken token)
    {
        var games = LoadGames();
        PowerProfileMode? lastMode = null;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var idle = GetIdleTime();
            var game = FindRunningGame(games);
            var desired = idle >= TimeSpan.FromMinutes(15)
                ? PowerProfileMode.Afk
                : game.Length > 0 ? PowerProfileMode.Performance : PowerProfileMode.Balanced;

            var target = PowerProfileService.GetTargetGuid(desired);
            var active = PowerProfileService.GetActiveSchemeGuid();
            if (!string.Equals(active, target, StringComparison.OrdinalIgnoreCase))
                PowerProfileService.Apply(desired);

            if (lastMode != desired)
            {
                onStatus?.Invoke(new AutoProfileStatus(desired, game.Length > 0, idle, game));
                lastMode = desired;
            }
            await Task.Delay(TimeSpan.FromSeconds(15), token);
        }
    }

    private static string[] LoadGames()
    {
        try
        {
            if (!File.Exists(GamesPath)) return DefaultGames;
            using var document = JsonDocument.Parse(File.ReadAllText(GamesPath));
            var names = document.RootElement.GetProperty("processNames").EnumerateArray()
                .Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return names.Length > 0 ? names : DefaultGames;
        }
        catch (Exception ex)
        {
            AppLog.Write("Lista de jogos inválida; usando padrão: " + ex.Message);
            return DefaultGames;
        }
    }

    private static string FindRunningGame(IEnumerable<string> processNames)
    {
        foreach (var name in processNames)
        {
            try
            {
                var processes = Process.GetProcessesByName(name);
                if (processes.Length > 0)
                {
                    foreach (var process in processes) process.Dispose();
                    return name;
                }
                foreach (var process in processes) process.Dispose();
            }
            catch { }
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
