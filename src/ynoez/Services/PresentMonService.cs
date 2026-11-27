using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace ynoez.Services;

public sealed record CaptureTarget(int ProcessId, string ProcessName)
{
    public string DisplayName => ProcessName + " (PID " + ProcessId + ")";
}

public sealed record FrameBenchmarkSummary(
    string ProcessName, int ProcessId, string CsvPath, int FrameCount,
    double AverageFps, double OnePercentLowFps, double AverageFrameTimeMs,
    double P99FrameTimeMs, int FramesOver33Ms, int FramesOver50Ms);

public static class PresentMonService
{
    private const string Version = "2.6.0";
    private const string Sha256 = "b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af";
    private const string DownloadUrl = "https://github.com/GameTechDev/PresentMon/releases/download/v2.6.0/PresentMon-2.6.0-x64.exe";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(45) };
    private static readonly string[] DefaultTargets =
    {
        "RobloxPlayerBeta", "VALORANT-Win64-Shipping", "cs2", "FortniteClient-Win64-Shipping",
        "r5apex", "GTA5", "League of Legends", "Overwatch", "Minecraft", "javaw"
    };

    public static IReadOnlyList<CaptureTarget> GetRunningTargets()
    {
        var targets = new List<CaptureTarget>();
        foreach (var name in LoadTargetNames())
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    using (process)
                    {
                        try { targets.Add(new CaptureTarget(process.Id, process.ProcessName)); }
                        catch { }
                    }
                }
            }
            catch { }
        }
        return targets.OrderBy(x => x.ProcessName, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.ProcessId).ToArray();
    }

    public static async Task<FrameBenchmarkSummary> CaptureAsync(
        CaptureTarget target, int durationSeconds, Action<string>? onStatus, CancellationToken token)
    {
        if (durationSeconds is not 30 and not 60 and not 120 and not 180)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds), "Duração permitida: 30, 60, 120 ou 180 segundos.");

        using (Process.GetProcessById(target.ProcessId)) { }
        onStatus?.Invoke("Verificando PresentMon " + Version + "…");
        var executable = await EnsurePresentMonAsync(token);

        Directory.CreateDirectory(AppPaths.Logs);
        var safeName = string.Concat(target.ProcessName.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-'));
        var csv = Path.Combine(AppPaths.Logs, "benchmark-" + safeName + "-" +
            DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv");
        var session = "ynoez-" + target.ProcessId + "-" + Guid.NewGuid().ToString("N")[..8];
        var args = "--process_id " + target.ProcessId.ToString(CultureInfo.InvariantCulture) +
            " --output_file " + Quote(csv) +
            " --timed " + durationSeconds.ToString(CultureInfo.InvariantCulture) +
            " --terminate_after_timed --terminate_on_proc_exit --exclude_dropped --date_time --session_name " + session;

        var startInfo = new ProcessStartInfo(executable, args)
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true
        };
        if (!IsAdministrator()) startInfo.Verb = "runas";

        onStatus?.Invoke("Capturando frames de " + target.ProcessName + " por " + durationSeconds + " segundos…");
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Não foi possível iniciar o PresentMon.");

        using var registration = token.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch { }
        });

        await process.WaitForExitAsync();
        token.ThrowIfCancellationRequested();

        if (!File.Exists(csv) || new FileInfo(csv).Length < 100)
            throw new InvalidOperationException("PresentMon não gerou dados. Verifique se o processo ainda estava aberto e se a permissão de captura foi concedida.");

        onStatus?.Invoke("Calculando FPS médio e 1% low…");
        var summary = ParseCsv(csv, target);
        AppLog.Write("Benchmark concluído: " + summary.ProcessName + " avg " +
            summary.AverageFps.ToString("0.0", CultureInfo.InvariantCulture) + " FPS, 1% low " +
            summary.OnePercentLowFps.ToString("0.0", CultureInfo.InvariantCulture) + " FPS");
        return summary;
    }

    private static async Task<string> EnsurePresentMonAsync(CancellationToken token)
    {
        var directory = Path.Combine(AppPaths.Toolbox, "Performance");
        Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, "PresentMon.exe");
        if (File.Exists(executable))
        {
            var existingHash = await HashFileAsync(executable, token);
            if (string.Equals(existingHash, Sha256, StringComparison.OrdinalIgnoreCase)) return executable;
            throw new InvalidDataException("O PresentMon existente não corresponde ao SHA-256 oficial. O arquivo foi preservado; verifique ou remova manualmente antes de tentar novamente.");
        }

        var temp = executable + ".download";
        try
        {
            await using (var source = await Http.GetStreamAsync(DownloadUrl, token))
            await using (var destination = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                await source.CopyToAsync(destination, token);

            var downloadedHash = await HashFileAsync(temp, token);
            if (!string.Equals(downloadedHash, Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Falha na verificação SHA-256 do PresentMon oficial.");
            File.Move(temp, executable, overwrite: false);
            return executable;
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, token);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static FrameBenchmarkSummary ParseCsv(string path, CaptureTarget target)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var header = reader.ReadLine() ?? throw new InvalidDataException("CSV do PresentMon sem cabeçalho.");
        var columns = SplitCsv(header);
        var frameIndex = columns.FindIndex(x => x.Equals("MsBetweenPresents", StringComparison.OrdinalIgnoreCase));
        if (frameIndex < 0)
            frameIndex = columns.FindIndex(x => x.Equals("MsBetweenDisplayChange", StringComparison.OrdinalIgnoreCase));
        if (frameIndex < 0)
            throw new InvalidDataException("A versão do PresentMon não retornou a coluna de tempo entre frames esperada.");

        var frameTimes = new List<double>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var fields = SplitCsv(line);
            if (frameIndex >= fields.Count) continue;
            if (double.TryParse(fields[frameIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms)
                && ms > 0 && ms < 1000)
                frameTimes.Add(ms);
        }

        if (frameTimes.Count < 30)
            throw new InvalidDataException("A captura teve menos de 30 frames válidos. Deixe o jogo renderizar por mais tempo e tente novamente.");

        frameTimes.Sort();
        var averageFrameTime = frameTimes.Average();
        var p99Index = Math.Clamp((int)Math.Ceiling(frameTimes.Count * 0.99) - 1, 0, frameTimes.Count - 1);
        var p99 = frameTimes[p99Index];
        return new FrameBenchmarkSummary(
            target.ProcessName, target.ProcessId, path, frameTimes.Count,
            1000d / averageFrameTime, 1000d / p99, averageFrameTime, p99,
            frameTimes.Count(x => x > 33.333), frameTimes.Count(x => x > 50));
    }

    private static List<string> SplitCsv(string line)
    {
        var fields = new List<string>();
        var value = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    value.Append('"');
                    i++;
                }
                else quoted = !quoted;
            }
            else if (ch == ',' && !quoted)
            {
                fields.Add(value.ToString());
                value.Clear();
            }
            else value.Append(ch);
        }
        fields.Add(value.ToString());
        return fields;
    }

    private static string[] LoadTargetNames()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Optimization", "games.json");
            if (!File.Exists(path)) return DefaultTargets;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var names = document.RootElement.GetProperty("processNames").EnumerateArray()
                .Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return names.Length > 0 ? names : DefaultTargets;
        }
        catch { return DefaultTargets; }
    }

    private static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
}
