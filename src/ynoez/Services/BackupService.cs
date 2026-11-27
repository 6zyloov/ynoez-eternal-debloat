using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Security.Cryptography;

namespace ynoez.Services;

public sealed record BackupFileInfo(string Name, long Length, string Sha256);

public sealed class BackupResult
{
    public string Folder { get; init; } = "";
    public string PowerSchemeGuid { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public IReadOnlyList<BackupFileInfo> Files { get; init; } = Array.Empty<BackupFileInfo>();
}

public static class BackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<string> CreateLocalSnapshotAsync()
    {
        Directory.CreateDirectory(AppPaths.Backups);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var folder = Path.Combine(AppPaths.Backups, $"snapshot-{stamp}-{suffix}");
        Directory.CreateDirectory(folder);

        try
        {
            var hardware = await HardwareService.GetSnapshotAsync();
            await WriteTextAtomicallyAsync(Path.Combine(folder, "hardware.json"),
                JsonSerializer.Serialize(hardware, JsonOptions));

            var power = RunCommand("powercfg.exe", "/getactivescheme");
            await WriteTextAtomicallyAsync(Path.Combine(folder, "active-power-plan.txt"),
                power.Output + Environment.NewLine + power.Error);

            var network = RunCommand("ipconfig.exe", "/all");
            await WriteTextAtomicallyAsync(Path.Combine(folder, "network-ipconfig.txt"),
                network.Output + Environment.NewLine + network.Error);

            var files = await BuildSnapshotManifestAsync(folder);
            var result = new BackupResult
            {
                Folder = folder,
                PowerSchemeGuid = ExtractGuid(power.Output),
                CreatedAt = DateTime.UtcNow,
                Files = files
            };
            await WriteTextAtomicallyAsync(Path.Combine(folder, "backup.json"),
                JsonSerializer.Serialize(result, JsonOptions));
            AppLog.Write($"Snapshot local criado e verificado: {folder} ({files.Count} arquivos com SHA-256).");
            return folder;
        }
        catch (Exception ex)
        {
            try
            {
                var root = Path.GetFullPath(AppPaths.Backups) + Path.DirectorySeparatorChar;
                var fullFolder = Path.GetFullPath(folder);
                if (fullFolder.StartsWith(root, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullFolder))
                    Directory.Delete(fullFolder, recursive: true);
            }
            catch (Exception cleanupError)
            {
                AppLog.Write("Falha ao remover snapshot incompleto: " + cleanupError.Message);
            }
            AppLog.Write("Criação do snapshot falhou: " + ex);
            throw;
        }
    }

    public static async Task<IReadOnlyList<BackupFileInfo>> BuildSnapshotManifestAsync(string folder)
    {
        var fullFolder = Path.GetFullPath(folder);
        if (!Directory.Exists(fullFolder))
            throw new DirectoryNotFoundException("A pasta do snapshot não existe.");

        var result = new List<BackupFileInfo>();
        foreach (var path in Directory.EnumerateFiles(fullFolder, "*", SearchOption.TopDirectoryOnly)
                     .Where(path => !string.Equals(Path.GetFileName(path), "backup.json", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
        {
            var info = new FileInfo(path);
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 64 * 1024, useAsync: true);
            var hash = await SHA256.HashDataAsync(stream);
            result.Add(new BackupFileInfo(info.Name, info.Length, Convert.ToHexString(hash).ToLowerInvariant()));
        }
        return result;
    }

    private static async Task WriteTextAtomicallyAsync(string path, string content)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, content);
            File.Move(temp, path, overwrite: false);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
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
