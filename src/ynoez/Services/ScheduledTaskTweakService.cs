using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace ynoez.Services;

public sealed record ScheduledTaskDefinition(
    string TaskPath, string TaskName, string DisplayName, string Description, string Risk);

public sealed record ScheduledTaskStatus(
    ScheduledTaskDefinition Definition, bool Available, string State, bool DisabledByYnoez);

public sealed record ScheduledTaskBackup(
    string TaskPath, string TaskName, string OriginalState, DateTimeOffset DisabledAt);

public sealed class ScheduledTaskTweakService
{
    private static readonly ScheduledTaskDefinition[] Catalog =
    {
        new(@"\Microsoft\Windows\Customer Experience Improvement Program\", "Consolidator",
            "Consolidação de telemetria do Windows",
            "Executa a consolidação de dados do Programa de Aperfeiçoamento da Experiência do Usuário. Desativar reduz uma tarefa de coleta/telemetria; não promete ganho perceptível de FPS.", "Baixo"),
        new(@"\Microsoft\Windows\Customer Experience Improvement Program\", "UsbCeip",
            "Telemetria de dispositivos USB",
            "Participa do programa de experiência do cliente relacionado a dispositivos USB. Não altera drivers, portas USB ou funcionamento dos periféricos.", "Baixo"),
        new(@"\Microsoft\Windows\Feedback\Siuf\", "DmClient",
            "Experiência de comentários do Windows",
            "Tarefa de coleta/feedback do Windows. Desativar reduz notificações/telemetria de feedback; não desativa atualização ou diagnóstico de falhas.", "Baixo"),
        new(@"\Microsoft\Windows\Feedback\Siuf\", "DmClientOnScenarioDownload",
            "Feedback após cenários de download",
            "Tarefa auxiliar de feedback do Windows. Não interfere no download de arquivos, navegador ou Windows Update.", "Baixo"),
        new(@"\Microsoft\Windows\Maps\", "MapsToastTask",
            "Notificações de mapas offline",
            "Tarefa de notificações de mapas offline. Pode reduzir avisos sobre mapas; não remove o aplicativo Mapas.", "Baixo"),
        new(@"\Microsoft\Windows\Maps\", "MapsUpdateTask",
            "Atualização de mapas offline",
            "Impede a atualização automática de mapas offline. Mapas já baixados permanecem instalados, mas podem ficar desatualizados.", "Baixo")
    };

    private static readonly HashSet<string> Allowed = Catalog
        .Select(x => Key(x.TaskPath, x.TaskName))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _backupDirectory;

    public ScheduledTaskTweakService(string? backupDirectory = null)
    {
        _backupDirectory = backupDirectory ?? Path.Combine(AppPaths.Data, "ScheduledTasks");
        Directory.CreateDirectory(_backupDirectory);
    }

    public static IReadOnlyList<ScheduledTaskDefinition> GetCatalog() => Catalog;

    public IReadOnlyList<ScheduledTaskStatus> GetStatuses()
    {
        var result = new List<ScheduledTaskStatus>();
        foreach (var item in Catalog)
        {
            var state = QueryState(item);
            result.Add(new ScheduledTaskStatus(item, state != "NOTFOUND",
                state == "NOTFOUND" ? "Não encontrada" : state, File.Exists(GetBackupPath(item))));
        }
        return result;
    }

    public void Disable(ScheduledTaskDefinition definition)
    {
        Validate(definition);
        var backupPath = GetBackupPath(definition);
        if (File.Exists(backupPath))
            throw new InvalidOperationException("Já existe um backup desta tarefa. Restaure-a antes de aplicar novamente.");

        var state = QueryState(definition);
        if (state == "NOTFOUND") throw new InvalidOperationException("A tarefa não existe nesta edição do Windows.");
        if (!string.Equals(state, "Ready", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Somente tarefas no estado Ready podem ser desativadas. Estado atual: " + state);

        WriteAtomic(backupPath, JsonSerializer.Serialize(
            new ScheduledTaskBackup(definition.TaskPath, definition.TaskName, state, DateTimeOffset.UtcNow), JsonOptions));

        var script = BuildCommand(definition, "Disable-ScheduledTask");
        try
        {
            RunElevated(script);
            var after = QueryState(definition);
            if (!string.Equals(after, "Disabled", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("O Windows não confirmou a desativação da tarefa. O backup foi mantido.");
            AppLog.Write("Tarefa agendada opcional desativada: " + Key(definition.TaskPath, definition.TaskName));
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            try { File.Delete(backupPath); } catch { }
            throw new OperationCanceledException("A elevação foi cancelada. Nenhuma alteração foi aplicada.", ex);
        }
    }

    public void Restore(ScheduledTaskDefinition definition)
    {
        Validate(definition);
        var backupPath = GetBackupPath(definition);
        if (!File.Exists(backupPath))
            throw new InvalidOperationException("Não existe backup de estado para esta tarefa.");

        var backup = JsonSerializer.Deserialize<ScheduledTaskBackup>(File.ReadAllText(backupPath), JsonOptions)
            ?? throw new InvalidDataException("Backup de tarefa inválido.");
        if (!string.Equals(backup.TaskPath, definition.TaskPath, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(backup.TaskName, definition.TaskName, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(backup.OriginalState, "Ready", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("O backup não corresponde a uma tarefa habilitada da lista autorizada.");

        var current = QueryState(definition);
        if (current == "NOTFOUND") throw new InvalidOperationException("A tarefa foi removida do Windows; o backup foi preservado.");
        if (string.Equals(current, "Disabled", StringComparison.OrdinalIgnoreCase))
        {
            RunElevated(BuildCommand(definition, "Enable-ScheduledTask"));
            current = QueryState(definition);
        }

        if (!string.Equals(current, "Ready", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A restauração não foi confirmada. O backup foi mantido. Estado atual: " + current);

        File.Delete(backupPath);
        AppLog.Write("Tarefa agendada restaurada: " + Key(definition.TaskPath, definition.TaskName));
    }

    private string GetBackupPath(ScheduledTaskDefinition definition)
    {
        Validate(definition);
        return Path.Combine(_backupDirectory, MakeId(definition) + ".json");
    }

    private static void Validate(ScheduledTaskDefinition definition)
    {
        if (!Allowed.Contains(Key(definition.TaskPath, definition.TaskName)))
            throw new InvalidOperationException("Esta tarefa não pertence à lista de tarefas opcionais permitidas.");
        if (!Catalog.Any(x => string.Equals(Key(x.TaskPath, x.TaskName), Key(definition.TaskPath, definition.TaskName), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Definição de tarefa inválida.");
    }

    private static string QueryState(ScheduledTaskDefinition definition)
    {
        var path = Escape(definition.TaskPath);
        var name = Escape(definition.TaskName);
        var script = "$ErrorActionPreference='Stop'; Import-Module ScheduledTasks -ErrorAction Stop; " +
            "$t=Get-ScheduledTask -TaskPath '" + path + "' -TaskName '" + name + "' -ErrorAction SilentlyContinue; " +
            "if ($null -eq $t) { 'NOTFOUND'; exit 0 }; [string]$t.State";
        var result = RunPowerShell(script, elevated: false);
        if (result.ExitCode != 0)
        {
            AppLog.Write("Consulta de tarefa falhou (" + definition.TaskName + "): " + result.Error);
            return "Indisponível";
        }
        return result.Output.Trim();
    }

    private static string BuildCommand(ScheduledTaskDefinition definition, string verb)
    {
        var path = Escape(definition.TaskPath);
        var name = Escape(definition.TaskName);
        return "$ErrorActionPreference='Stop'; Import-Module ScheduledTasks -ErrorAction Stop; " +
            verb + " -TaskPath '" + path + "' -TaskName '" + name + "' -ErrorAction Stop | Out-Null; " +
            "$t=Get-ScheduledTask -TaskPath '" + path + "' -TaskName '" + name + "' -ErrorAction Stop; " +
            "if ('" + verb + "' -eq 'Disable-ScheduledTask' -and [string]$t.State -ne 'Disabled') { exit 3 }; " +
            "if ('" + verb + "' -eq 'Enable-ScheduledTask' -and [string]$t.State -eq 'Disabled') { exit 4 }; exit 0";
    }

    private static string MakeId(ScheduledTaskDefinition definition) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes(Key(definition.TaskPath, definition.TaskName).ToUpperInvariant()))).ToLowerInvariant();

    private static string Key(string taskPath, string taskName) => taskPath.TrimEnd('\\') + "\\" + taskName;

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static void RunElevated(string script)
    {
        var result = RunPowerShell(script, elevated: true);
        if (result.ExitCode != 0)
            throw new InvalidOperationException("O Windows recusou a alteração da tarefa (código " + result.ExitCode + "): " + result.Error);
    }

    private static (int ExitCode, string Output, string Error) RunPowerShell(string script, bool elevated)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var isAdmin = false;
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            isAdmin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("powershell.exe",
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded)
            {
                UseShellExecute = elevated,
                Verb = elevated && !isAdmin ? "runas" : "",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = !elevated,
                RedirectStandardOutput = !elevated,
                RedirectStandardError = !elevated
            }
        };
        process.Start();
        if (elevated)
        {
            process.WaitForExit();
            return (process.ExitCode, "", "");
        }

        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("A consulta da tarefa excedeu 15 segundos.");
        }
        return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }

    private void WriteAtomic(string path, string content)
    {
        Directory.CreateDirectory(_backupDirectory);
        var temp = path + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, overwrite: true);
    }
}
