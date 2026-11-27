using System.IO;
using System.Diagnostics;
using System.Text.Json;

namespace ynoez.Services;

public sealed class BackupResult
{
    public string Folder { get; init; } = "";
    public string PowerSchemeGuid { get; init; } = "";
    public DateTime CreatedAt { get; init; }
}

public static class BackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<string> CreateLocalSnapshotAsync()
    {
        Directory.CreateDirectory(AppPaths.Backups);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var folder = Path.Combine(AppPaths.Backups, $"snapshot-{stamp}");
        Directory.CreateDirectory(folder);

        var hardware = await HardwareService.GetSnapshotAsync();
        await File.WriteAllTextAsync(Path.Combine(folder, "hardware.json"), JsonSerializer.Serialize(hardware, JsonOptions));
        var power = RunCommand("powercfg.exe", "/getactivescheme");
        await File.WriteAllTextAsync(Path.Combine(folder, "active-power-plan.txt"), power.Output + Environment.NewLine + power.Error);
        var network = RunCommand("ipconfig.exe", "/all");
        await File.WriteAllTextAsync(Path.Combine(folder, "network-ipconfig.txt"), network.Output + Environment.NewLine + network.Error);

        var schemeGuid = ExtractGuid(power.Output);
        var result = new BackupResult { Folder = folder, PowerSchemeGuid = schemeGuid, CreatedAt = DateTime.Now };
        await File.WriteAllTextAsync(Path.Combine(folder, "backup.json"), JsonSerializer.Serialize(result, JsonOptions));
        AppLog.Write($"Snapshot local criado: {folder}");
        return folder;
    }

    public static async Task<string> CreateRestorePointAsync()
    {
        var script = "Checkpoint-Computer -Description 'ynoez restore point' -RestorePointType 'MODIFY_SETTINGS'";
        var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
        var info = new ProcessStartInfo("powershell.exe")
        {
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Não foi possível iniciar o PowerShell elevado.");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException("O Windows não criou o ponto de restauração. Verifique se a Proteção do Sistema está ativada.");
        AppLog.Write("Ponto de restauração do Windows solicitado.");
        return "O Windows concluiu o comando de criação do ponto de restauração.";
    }

    public static string GetCurrentPowerSchemeGuid()
    {
        var result = RunCommand("powercfg.exe", "/getactivescheme");
        return ExtractGuid(result.Output);
    }

    public static void SavePowerSchemeBeforeChange(string guid)
    {
        var file = Path.Combine(AppPaths.Data, "previous-power-plan.json");
        File.WriteAllText(file, JsonSerializer.Serialize(new { SavedAt = DateTime.Now, Guid = guid }, JsonOptions));
    }

    public static string RestorePreviousPowerScheme()
    {
        var file = Path.Combine(AppPaths.Data, "previous-power-plan.json");
        if (!File.Exists(file)) return "Não existe um plano de energia anterior salvo pelo ynoez.";
        using var json = JsonDocument.Parse(File.ReadAllText(file));
        var guid = json.RootElement.GetProperty("Guid").GetString();
        if (string.IsNullOrWhiteSpace(guid)) return "O backup do plano de energia está inválido.";
        var result = RunCommand("powercfg.exe", $"/setactive {guid}");
        AppLog.Write($"Plano de energia restaurado: {guid}");
        if (result.ExitCode != 0) return "Falha ao restaurar o plano: " + result.Error;
        try { File.Delete(file); } catch { }
        return "Plano de energia anterior restaurado. O registro antigo foi consumido; uma próxima alteração salvará o novo plano base.";
    }

    public static string SetHighPerformancePlan()
    {
        var previous = GetCurrentPowerSchemeGuid();
        if (string.IsNullOrWhiteSpace(previous)) throw new InvalidOperationException("Não foi possível identificar o plano de energia atual.");
        var highPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        if (!string.Equals(previous, highPerformanceGuid, StringComparison.OrdinalIgnoreCase)
            && !File.Exists(Path.Combine(AppPaths.Data, "previous-power-plan.json")))
            SavePowerSchemeBeforeChange(previous);
        var result = RunCommand("powercfg.exe", "/setactive SCHEME_MIN");
        if (result.ExitCode != 0) throw new InvalidOperationException("Não foi possível ativar Alto desempenho: " + result.Error);
        AppLog.Write("Plano Alto desempenho ativado com backup prévio.");
        return "Plano Alto desempenho ativado. O plano anterior foi salvo para restauração.";
    }

    public static IReadOnlyList<string> ListBackups()
    {
        if (!Directory.Exists(AppPaths.Backups)) return Array.Empty<string>();
        return Directory.GetDirectories(AppPaths.Backups, "snapshot-*")
            .OrderByDescending(Directory.GetCreationTimeUtc).ToArray();
    }

    private static (int ExitCode, string Output, string Error) RunCommand(string file, string arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo(file, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }

    private static string ExtractGuid(string text)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text, @"[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}");
        return match.Success ? match.Value : "";
    }
}
