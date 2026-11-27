using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Security.Cryptography;

namespace ynoez.Services;

public sealed record BackupFileInfo(string Name, long Length, string Sha256);
public sealed record SnapshotVerificationResult(bool IsValid, int VerifiedFiles, string Details);

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

    public static async Task<SnapshotVerificationResult> VerifyLocalSnapshotAsync(
        string folder, string? backupsDirectory = null)
    {
        var root = Path.GetFullPath(backupsDirectory ?? AppPaths.Backups).TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullFolder = Path.GetFullPath(folder);
        if (!string.Equals(Path.GetDirectoryName(fullFolder), root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("O snapshot precisa estar diretamente dentro da pasta Backups do ynoez.");
        if (!Directory.Exists(fullFolder))
            throw new DirectoryNotFoundException("A pasta do snapshot não existe.");

        var manifestPath = Path.Combine(fullFolder, "backup.json");
        if (!File.Exists(manifestPath))
            return new SnapshotVerificationResult(false, 0, "Manifesto backup.json ausente. O snapshot pode estar incompleto ou ser de uma versão antiga.");

        BackupResult manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<BackupResult>(await File.ReadAllTextAsync(manifestPath), JsonOptions)
                ?? throw new InvalidDataException("Manifesto de backup vazio.");
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            return new SnapshotVerificationResult(false, 0, "Manifesto inválido: " + ex.Message);
        }

        if (string.IsNullOrWhiteSpace(manifest.Folder) ||
            !string.Equals(Path.GetFullPath(manifest.Folder), fullFolder, StringComparison.OrdinalIgnoreCase))
            return new SnapshotVerificationResult(false, 0, "O caminho registrado no manifesto não corresponde à pasta selecionada.");
        if (manifest.Files is null || manifest.Files.Count == 0)
            return new SnapshotVerificationResult(false, 0, "Este snapshot não contém hashes de integridade (formato antigo ou manifesto vazio).");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var issues = new List<string>();
        var verified = 0;
        foreach (var file in manifest.Files)
        {
            if (string.IsNullOrWhiteSpace(file.Name) ||
                file.Name is "." or ".." ||
                !string.Equals(Path.GetFileName(file.Name), file.Name, StringComparison.Ordinal) ||
                string.Equals(file.Name, "backup.json", StringComparison.OrdinalIgnoreCase) ||
                !names.Add(file.Name))
            {
                issues.Add("Nome de arquivo inválido ou duplicado no manifesto.");
                continue;
            }
            if (file.Length < 0 || file.Sha256.Length != 64 ||
                !file.Sha256.All(Uri.IsHexDigit))
            {
                issues.Add(file.Name + ": metadados de hash/tamanho inválidos.");
                continue;
            }

            var path = Path.Combine(fullFolder, file.Name);
            if (!File.Exists(path))
            {
                issues.Add(file.Name + ": arquivo ausente.");
                continue;
            }

            var info = new FileInfo(path);
            if (info.Length != file.Length)
            {
                issues.Add(file.Name + ": tamanho diferente do manifesto.");
                continue;
            }

            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 64 * 1024, useAsync: true);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
            if (!string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(file.Name + ": SHA-256 não corresponde.");
                continue;
            }
            verified++;
        }

        var actualNames = Directory.EnumerateFiles(fullFolder, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => !string.Equals(name, "backup.json", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var untracked in actualNames.Except(names, StringComparer.OrdinalIgnoreCase))
            issues.Add(untracked + ": arquivo fora do manifesto.");

        return new SnapshotVerificationResult(
            issues.Count == 0 && verified == manifest.Files.Count,
            verified,
            issues.Count == 0
                ? "Integridade confirmada: " + verified + " arquivos correspondem ao tamanho e ao SHA-256 do manifesto."
                : string.Join(Environment.NewLine, issues));
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

    private static string LegacyPowerPlanBackupPath =>
        Path.Combine(AppPaths.Data, "previous-power-plan.json");

    public static bool HasPreviousPowerSchemeBackup => File.Exists(LegacyPowerPlanBackupPath);

    public static string RestorePreviousPowerScheme()
    {
        var file = LegacyPowerPlanBackupPath;
        if (!File.Exists(file))
            return "Não existe um backup de plano de energia da versão antiga do ynoez.";

        using var json = JsonDocument.Parse(File.ReadAllText(file));
        var guidText = json.RootElement.TryGetProperty("Guid", out var guidElement)
            ? guidElement.GetString() : null;
        if (!Guid.TryParse(guidText, out var parsed))
            throw new InvalidDataException("O GUID do backup antigo de energia é inválido; o arquivo foi preservado.");

        var guid = parsed.ToString();
        var result = RunCommand("powercfg.exe", "/setactive " + guid);
        if (result.ExitCode != 0)
            throw new InvalidOperationException("Falha ao solicitar a restauração do plano antigo: " + result.Error);

        var active = GetCurrentPowerSchemeGuid();
        if (!string.Equals(active, guid, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("O Windows não confirmou o plano antigo. O backup foi mantido.");

        File.Delete(file);
        AppLog.Write("Plano de energia legado restaurado e verificado: " + guid);
        return "Plano de energia legado restaurado e verificado.";
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
