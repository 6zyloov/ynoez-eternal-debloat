using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ynoez.Services;

public sealed record ScheduledTaskSafetyClassification(
    string Category,
    bool IsProtected,
    bool IsOptional,
    string Explanation);

public sealed record WindowsScheduledTaskInfo(
    string TaskPath,
    string TaskName,
    string State,
    string Author,
    string Description,
    string LastRunTime,
    string NextRunTime,
    string LastTaskResult,
    ScheduledTaskSafetyClassification Safety)
{
    public string FullName => TaskPath + TaskName;
    public string StateDisplay => State switch
    {
        "Ready" => "Pronta",
        "Running" => "Em execução",
        "Disabled" => "Desativada",
        "Queued" => "Na fila",
        _ => string.IsNullOrWhiteSpace(State) ? "Desconhecido" : State
    };
    public string LastRunDisplay => WindowsScheduledTaskInventoryService.FormatDate(LastRunTime, "Nunca executada");
    public string NextRunDisplay => WindowsScheduledTaskInventoryService.FormatDate(NextRunTime, "Não agendada");
    public string LastResultDisplay => string.IsNullOrWhiteSpace(LastTaskResult) ? "Não reportado" : LastTaskResult;
    public string SafetyDisplay => Safety.Category;
}

public static class WindowsScheduledTaskInventoryService
{
    private static readonly string[] ProtectedPathPrefixes =
    {
        @"\Microsoft\Windows\Windows Defender\",
        @"\Microsoft\Windows\WindowsUpdate\",
        @"\Microsoft\Windows\UpdateOrchestrator\",
        @"\Microsoft\Windows\WaaSMedic\",
        @"\Microsoft\Windows\TaskScheduler\",
        @"\Microsoft\Windows\Servicing\",
        @"\Microsoft\Windows\Diagnosis\",
        @"\Microsoft\Windows\DiskDiagnostic\",
        @"\Microsoft\Windows\Chkdsk\",
        @"\Microsoft\Windows\Defrag\",
        @"\Microsoft\Windows\Search\",
        @"\Microsoft\Windows\Shell\",
        @"\Microsoft\Windows\NetworkProfile\",
        @"\Microsoft\Windows\NlaSvc\",
        @"\Microsoft\Windows\WlanSvc\",
        @"\Microsoft\Windows\Power Efficiency Diagnostics\",
        @"\Microsoft\Windows\Customer Experience Improvement Program\"
    };

    private static readonly string[] ProtectedNameTokens =
    {
        "Vanguard", "FACEIT", "EasyAntiCheat", "BattlEye", "AntiCheat"
    };

    private static readonly HashSet<string> OptionalTasks =
        ScheduledTaskTweakService.GetCatalog()
            .Select(x => NormalizeKey(x.TaskPath, x.TaskName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static ScheduledTaskSafetyClassification ClassifyTask(string taskPath, string taskName)
    {
        if (string.IsNullOrWhiteSpace(taskPath) || string.IsNullOrWhiteSpace(taskName))
            throw new ArgumentException("Caminho e nome da tarefa são obrigatórios.");

        var path = taskPath.Trim();
        var name = taskName.Trim();
        if (OptionalTasks.Contains(NormalizeKey(path, name)))
        {
            return new ScheduledTaskSafetyClassification(
                "Opcional avaliada", false, true,
                "Tarefa presente na whitelist restrita de Debloat. Qualquer alteração fica separada, com confirmação, backup, verificação e restauração.");
        }

        if (ProtectedPathPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) ||
            ProtectedNameTokens.Any(token =>
                name.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                path.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            return new ScheduledTaskSafetyClassification(
                "Protegida pelo ynoez", true, false,
                "Tarefa de manutenção, atualização, diagnóstico, rede, pesquisa ou anti-cheat. A Toolbox não oferece alterações.");
        }

        return new ScheduledTaskSafetyClassification(
            "Somente leitura", false, false,
            "Não existe uma política de alteração aprovada para esta tarefa. O inventário apenas consulta os metadados e abre o Agendador oficial.");
    }

    public static async Task<IReadOnlyList<WindowsScheduledTaskInfo>> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(AppPaths.Data);
        var outputPath = Path.Combine(AppPaths.Data, "scheduled-task-inventory-" + Guid.NewGuid().ToString("N") + ".json");
        var script = BuildReadOnlyScript(outputPath);

        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(AppPaths.PowerShellExecutable,
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden
            }
        };

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            using var registration = timeout.Token.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch { }
            });

            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("A consulta das tarefas agendadas excedeu 45 segundos.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var error = await stderr.ConfigureAwait(false);
            _ = await stdout.ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    "O Windows não conseguiu consultar o Agendador de Tarefas (código " +
                    process.ExitCode.ToString(CultureInfo.InvariantCulture) + "): " + error.Trim());

            if (!File.Exists(outputPath))
                throw new InvalidDataException("O Windows não criou o relatório esperado das tarefas agendadas.");
            var file = new FileInfo(outputPath);
            if (file.Length == 0 || file.Length > 30 * 1024 * 1024)
                throw new InvalidDataException("O relatório de tarefas está vazio ou excede o limite de 30 MB.");

            return ParseJson(await File.ReadAllTextAsync(outputPath, cancellationToken).ConfigureAwait(false));
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException("A consulta foi cancelada pelo usuário. Nenhuma tarefa foi alterada.", ex, cancellationToken);
        }
        finally
        {
            try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
        }
    }

    internal static string BuildReadOnlyScript(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Caminho do relatório vazio.", nameof(outputPath));

        var safePath = outputPath.Replace("'", "''", StringComparison.Ordinal);
        return
            "$ErrorActionPreference='Stop'; Import-Module ScheduledTasks -ErrorAction Stop; " +
            "$rows=@(Get-ScheduledTask -ErrorAction Stop | ForEach-Object { " +
                "$t=$_; $i=$null; try { $i=Get-ScheduledTaskInfo -InputObject $t -ErrorAction Stop } catch {}; " +
                "$last=''; $next=''; $result=''; " +
                "if ($null -ne $i) { " +
                    "if ($i.LastRunTime -gt [datetime]::MinValue) { $last=$i.LastRunTime.ToString('o') }; " +
                    "if ($i.NextRunTime -gt [datetime]::MinValue) { $next=$i.NextRunTime.ToString('o') }; " +
                    "$result=[string]$i.LastTaskResult " +
                "}; " +
                "[pscustomobject]@{TaskPath=[string]$t.TaskPath;TaskName=[string]$t.TaskName;" +
                    "State=[string]$t.State;Author=[string]$t.Author;Description=[string]$t.Description;" +
                    "LastRunTime=$last;NextRunTime=$next;LastTaskResult=$result} " +
            "}); " +
            "ConvertTo-Json -InputObject $rows -Depth 3 -Compress | Set-Content -LiteralPath '" + safePath + "' -Encoding UTF8";
    }

    internal static IReadOnlyList<WindowsScheduledTaskInfo> ParseJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("O JSON do inventário de tarefas está vazio.");

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var elements = root.ValueKind switch
        {
            JsonValueKind.Array => root.EnumerateArray().ToArray(),
            JsonValueKind.Object => new[] { root },
            _ => throw new InvalidDataException("O relatório de tarefas precisa ser um objeto ou array JSON.")
        };

        var tasks = new List<WindowsScheduledTaskInfo>(elements.Length);
        foreach (var item in elements)
        {
            var path = ReadString(item, "TaskPath");
            var name = ReadString(item, "TaskName");
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(name))
                continue;

            tasks.Add(new WindowsScheduledTaskInfo(
                path,
                name,
                ReadString(item, "State"),
                ReadString(item, "Author"),
                ReadString(item, "Description"),
                ReadString(item, "LastRunTime"),
                ReadString(item, "NextRunTime"),
                ReadString(item, "LastTaskResult"),
                ClassifyTask(path, name)));
        }

        return tasks
            .OrderBy(x => x.TaskPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.TaskName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public static string FormatDate(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal, out var parsed)
            ? parsed.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.CurrentCulture)
            : value;
    }

    public static string BuildCsv(IEnumerable<WindowsScheduledTaskInfo> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        var rows = new List<string>
        {
            string.Join(",", new[]
            {
                EscapeCsv("TaskPath"),
                EscapeCsv("TaskName"),
                EscapeCsv("State"),
                EscapeCsv("SafetyCategory"),
                EscapeCsv("LastRunTime"),
                EscapeCsv("NextRunTime"),
                EscapeCsv("LastTaskResult"),
                EscapeCsv("Author"),
                EscapeCsv("Description")
            })
        };

        foreach (var task in tasks)
        {
            rows.Add(string.Join(",", new[]
            {
                EscapeCsv(task.TaskPath),
                EscapeCsv(task.TaskName),
                EscapeCsv(task.StateDisplay),
                EscapeCsv(task.Safety.Category),
                EscapeCsv(task.LastRunDisplay),
                EscapeCsv(task.NextRunDisplay),
                EscapeCsv(task.LastResultDisplay),
                EscapeCsv(task.Author),
                EscapeCsv(task.Description)
            }));
        }

        return string.Join(Environment.NewLine, rows) + Environment.NewLine;
    }

    public static string EscapeCsv(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static string NormalizeKey(string path, string name) =>
        (path.TrimEnd('\\') + "\\" + name).ToUpperInvariant();

    private static string ReadString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
            return string.Empty;
        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : value.ToString();
    }
}
