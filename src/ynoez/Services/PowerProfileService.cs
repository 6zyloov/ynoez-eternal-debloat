using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ynoez.Services;

public enum PowerProfileMode { Performance, Balanced, Afk }

public sealed record PowerProfileState(string Mode, string AppliedGuid, DateTimeOffset AppliedAt);
public sealed record PowerProfileBackup(string OriginalGuid, DateTimeOffset SavedAt, string? ExportPath = null);

public static class PowerProfileService
{
    private static readonly string BaseBackupPath = Path.Combine(AppPaths.Data, "power-profile-base.json");
    private static readonly string StatePath = Path.Combine(AppPaths.Data, "power-profile-state.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string GetActiveSchemeGuid()
    {
        var result = RunPowerCfg("/getactivescheme");
        var match = Regex.Match(result.Output, @"[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}");
        if (result.ExitCode != 0 || !match.Success)
            throw new InvalidOperationException("Não foi possível identificar o plano de energia ativo: " + result.Error);
        return match.Value;
    }

    public static string Apply(PowerProfileMode mode)
    {
        var targetGuid = GetTargetGuid(mode);
        var current = GetActiveSchemeGuid();
        if (string.Equals(current, targetGuid, StringComparison.OrdinalIgnoreCase))
            return "O plano solicitado já está ativo. Nenhuma alteração foi necessária.";

        if (File.Exists(BaseBackupPath))
        {
            var existing = JsonSerializer.Deserialize<PowerProfileBackup>(File.ReadAllText(BaseBackupPath), JsonOptions)
                ?? throw new InvalidDataException("Backup do plano base inválido; a alteração foi bloqueada.");
            if (!Regex.IsMatch(existing.OriginalGuid, @"^[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$"))
                throw new InvalidDataException("GUID no backup do plano base inválido; a alteração foi bloqueada.");
        }
        else
        {
            Directory.CreateDirectory(AppPaths.Backups);
            var exportPath = Path.Combine(AppPaths.Backups, "power-profile-base-" + current + ".pow");
            var export = RunPowerCfg("/export " + Quote(exportPath) + " " + current);
            if (export.ExitCode != 0 || !File.Exists(exportPath))
                throw new InvalidOperationException("Não foi possível exportar o plano original antes da alteração. Nenhum perfil foi aplicado. " + export.Error);
            WriteAtomic(BaseBackupPath, JsonSerializer.Serialize(new PowerProfileBackup(current, DateTimeOffset.UtcNow, exportPath), JsonOptions));
        }

        var result = RunPowerCfg("/setactive " + targetGuid);
        if (result.ExitCode != 0)
            throw new InvalidOperationException("O Windows não conseguiu alterar o plano de energia: " + result.Error);

        var active = GetActiveSchemeGuid();
        if (!string.Equals(active, targetGuid, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("O Windows não confirmou o plano solicitado; o backup original foi mantido.");

        var state = new PowerProfileState(mode.ToString(), active, DateTimeOffset.UtcNow);
        WriteAtomic(StatePath, JsonSerializer.Serialize(state, JsonOptions));
        AppLog.Write("Perfil de energia aplicado: " + mode + " (" + active + ")");
        return mode switch
        {
            PowerProfileMode.Performance => "Perfil Alto desempenho ativado. O plano original está exportado para restauração.",
            PowerProfileMode.Balanced => "Perfil Equilibrado ativado. O plano original está exportado para restauração.",
            PowerProfileMode.Afk => "Perfil Economia de energia ativado. Aplicativos e processos permanecem abertos.",
            _ => "Perfil aplicado."
        };
    }

    public static string RestoreOriginal()
    {
        if (!File.Exists(BaseBackupPath))
            return "Não existe um plano original salvo pelo gerenciador de perfis.";

        var backup = JsonSerializer.Deserialize<PowerProfileBackup>(File.ReadAllText(BaseBackupPath), JsonOptions)
            ?? throw new InvalidDataException("Backup do plano de energia inválido.");
        if (!Regex.IsMatch(backup.OriginalGuid, @"^[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$"))
            throw new InvalidDataException("GUID do plano original inválido.");

        var result = RunPowerCfg("/setactive " + backup.OriginalGuid);
        var active = "";
        if (result.ExitCode == 0)
        {
            try { active = GetActiveSchemeGuid(); }
            catch { active = ""; }
        }

        if (!string.Equals(active, backup.OriginalGuid, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(backup.ExportPath) && File.Exists(backup.ExportPath))
        {
            var import = RunPowerCfg("/import " + Quote(backup.ExportPath) + " " + backup.OriginalGuid);
            if (import.ExitCode != 0)
                throw new InvalidOperationException("O plano original não existe e o backup exportado não pôde ser importado: " + import.Error);
            result = RunPowerCfg("/setactive " + backup.OriginalGuid);
            if (result.ExitCode == 0) active = GetActiveSchemeGuid();
        }

        if (result.ExitCode != 0 || !string.Equals(active, backup.OriginalGuid, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Falha ao restaurar/verificar o plano original: " + result.Error);

        File.Delete(BaseBackupPath);
        if (File.Exists(StatePath)) File.Delete(StatePath);
        AppLog.Write("Plano de energia original restaurado: " + active);
        return "Plano de energia original restaurado e verificado.";
    }

    public static string GetTargetGuid(PowerProfileMode mode)
    {
        var alias = mode switch
        {
            PowerProfileMode.Performance => "SCHEME_MIN",
            PowerProfileMode.Balanced => "SCHEME_BALANCED",
            PowerProfileMode.Afk => "SCHEME_MAX",
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        var result = RunPowerCfg("/query " + alias);
        var match = Regex.Match(result.Output, @"[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}");
        if (result.ExitCode != 0 || !match.Success)
            throw new InvalidOperationException("Plano de energia não disponível: " + alias);
        return match.Value;
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

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    private static void WriteAtomic(string path, string content)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, overwrite: true);
    }
}
