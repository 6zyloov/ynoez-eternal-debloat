using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ynoez.Services;

public sealed record AsusFirmwareInspection(
    string ExecutablePath,
    IReadOnlyList<FirmwareDetectedSetting> Settings);

internal sealed record AsusFirmwareChange(
    string Name, string OriginalValue, string TargetValue);

internal sealed record AsusFirmwareJournal(
    string OriginalConfigPath,
    List<AsusFirmwareChange> Changes,
    DateTimeOffset CapturedAt);

/// <summary>
/// Uses ASUS's own ASUS Configuration Tool (ACT) only when it is installed.
/// ACT officially supports a limited set of commercial ASUS client devices;
/// settings are read dynamically from the current BIOS JSON and are never guessed.
/// </summary>
public static class AsusConfigurationToolAdapter
{
    private static readonly string JournalDirectory = Path.Combine(AppPaths.Data, "Firmware", "ASUS");
    private static readonly string JournalPath = Path.Combine(JournalDirectory, "pending.json");

    private sealed record ParsedSetting(string Name, JsonObject Node, string CurrentValue);

    public static bool TryInspect(out AsusFirmwareInspection? inspection)
    {
        inspection = null;
        var executable = FindExecutable();
        if (executable is null) return false;

        try
        {
            var json = ReadConfiguration(executable);
            var parsed = ParseSettings(json);
            if (parsed.Count == 0) return false;

            ResolvePendingJournal(parsed);
            inspection = new AsusFirmwareInspection(
                executable,
                parsed.Select(x => new FirmwareDetectedSetting(
                    x.Name,
                    x.CurrentValue,
                    ReadOptions(x.Node).Select(option => option.Prompt).ToArray())).ToArray());
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write("ASUS ACT encontrado, mas a leitura de BIOS não foi confirmada: " + ex.Message);
            return false;
        }
    }

    public static FirmwareApplyResult Apply(
        string executable,
        IReadOnlyList<string> requirementIds)
    {
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            throw new FileNotFoundException("ASUS Configuration Tool não está instalado neste computador.", executable);
        if (requirementIds.Count == 0)
            return new FirmwareApplyResult(false, false, 0, [], "Nenhum requisito de BIOS foi selecionado.");

        Directory.CreateDirectory(JournalDirectory);
        var originalJson = ReadConfiguration(executable);
        var settings = ParseSettings(originalJson);
        ResolvePendingJournal(settings);
        if (File.Exists(JournalPath))
            throw new InvalidOperationException(
                "Há uma alteração ASUS ACT pendente de reinicialização ou verificação. Reinicie e analise a BIOS novamente.");

        var changes = new List<AsusFirmwareChange>();
        var missing = new List<string>();
        var nodes = new List<(ParsedSetting Setting, JsonNode TargetNode)>();

        foreach (var requirement in requirementIds)
        {
            var settingName = FirmwareCompatibilityService.FindSettingName(
                settings.Select(x => x.Name), requirement);
            if (settingName is null)
            {
                missing.Add(requirement);
                continue;
            }

            var setting = settings.First(x =>
                string.Equals(x.Name, settingName, StringComparison.OrdinalIgnoreCase));
            var target = SelectEnabledOption(setting.Node, requirement);
            if (target is null)
            {
                missing.Add(requirement);
                continue;
            }

            var targetValue = NodeText(target["#text"]);
            var alreadyEnabled = IsAlreadyEnabled(setting, requirement, targetValue);
            if (alreadyEnabled) continue;

            changes.Add(new AsusFirmwareChange(setting.Name, setting.CurrentValue, targetValue));
            nodes.Add((setting, target["#text"]!.DeepClone()));
        }

        if (missing.Count > 0)
            return new FirmwareApplyResult(false, false, 0, [],
                "A BIOS ASUS não expôs opções seguras para todos os requisitos selecionados: " +
                string.Join(", ", missing) + ". Nenhuma configuração foi alterada.");

        if (changes.Count == 0)
            return new FirmwareApplyResult(false, false, 0, [],
                "Os recursos necessários já estão habilitados na BIOS. Nenhuma alteração foi necessária.");

        var originalPath = Path.Combine(JournalDirectory,
            "original-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".json");
        var modifiedPath = Path.Combine(JournalDirectory, "apply-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(originalPath, originalJson);

        var root = JsonNode.Parse(originalJson)
            ?? throw new InvalidDataException("O arquivo JSON de BIOS retornado pelo ASUS ACT está vazio.");

        foreach (var item in nodes)
        {
            var node = FindSettingNode(root, item.Setting.Name)
                ?? throw new InvalidDataException("A configuração mudou durante a análise: " + item.Setting.Name);
            node["@current"] = item.TargetNode.DeepClone();
        }

        var modifiedJson = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(modifiedPath, modifiedJson);

        WriteAtomic(JournalPath, JsonSerializer.Serialize(
            new AsusFirmwareJournal(originalPath, changes, DateTimeOffset.UtcNow),
            new JsonSerializerOptions { WriteIndented = true }));

        try
        {
            var exitCode = RunAct(executable, ["--set", "--input", modifiedPath], 90000);
            if (exitCode != 0)
                throw new InvalidOperationException("ASUS ACT recusou a configuração da BIOS (código " + exitCode + ").");

            AppLog.Write("ASUS ACT preparou alterações na BIOS: " +
                string.Join(", ", changes.Select(x => x.Name + "=" + x.TargetValue)) +
                ". Reinicialização necessária.");
            return new FirmwareApplyResult(true, true, changes.Count,
                changes.Select(x => x.Name).ToArray(),
                "ASUS ACT confirmou o envio das alterações à BIOS. Reinicie para aplicar; depois abra esta página novamente para verificar.");
        }
        catch (OperationCanceledException)
        {
            TryDelete(JournalPath);
            TryDelete(originalPath);
            TryDelete(modifiedPath);
            throw;
        }
        catch (Exception applyError)
        {
            try
            {
                var rollbackCode = RunAct(executable, ["--set", "--input", originalPath], 90000);
                if (rollbackCode != 0)
                    throw new InvalidOperationException("O ASUS ACT não confirmou a recuperação do arquivo original (código " + rollbackCode + ").");
                TryDelete(JournalPath);
                TryDelete(originalPath);
                TryDelete(modifiedPath);
            }
            catch (Exception rollbackError)
            {
                AppLog.Write("Falha ao restaurar a configuração ASUS original; arquivo preservado: " + rollbackError.Message);
                throw new InvalidOperationException(
                    "A BIOS recusou o perfil e a recuperação automática não foi confirmada. O arquivo original foi preservado em " +
                    originalPath + ". " + applyError.Message + " | " + rollbackError.Message, applyError);
            }
            throw;
        }
        finally
        {
            TryDelete(modifiedPath);
        }
    }

    private static string? FindExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "ASUS", "AsusConfigurationTool", "act.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "ASUS", "AsusConfigurationTool", "act.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string ReadConfiguration(string executable)
    {
        var path = Path.Combine(JournalDirectory, "read-" + Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(JournalDirectory);
        try
        {
            var exitCode = RunAct(executable, ["--get", "--output", path], 60000);
            if (exitCode != 0)
                throw new InvalidOperationException("ASUS ACT não conseguiu ler as configurações de BIOS (código " + exitCode + ").");
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
                throw new InvalidDataException("ASUS ACT não criou um arquivo de configuração de BIOS válido.");
            return File.ReadAllText(path);
        }
        finally
        {
            TryDelete(path);
        }
    }

    private static IReadOnlyList<ParsedSetting> ParseSettings(string json)
    {
        var root = JsonNode.Parse(json)
            ?? throw new InvalidDataException("JSON de BIOS ASUS inválido.");
        var result = new List<ParsedSetting>();
        CollectSettings(root, result);
        return result
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToArray();
    }

    private static void CollectSettings(JsonNode? node, List<ParsedSetting> result)
    {
        if (node is JsonObject obj)
        {
            var name = NodeText(obj["@prompt"]);
            var current = NodeText(obj["@current"]);
            if (!string.IsNullOrWhiteSpace(name) && current is not null && obj["option"] is JsonArray)
                result.Add(new ParsedSetting(name, obj, current));

            foreach (var value in obj.ToArray())
                CollectSettings(value.Value, result);
        }
        else if (node is JsonArray array)
        {
            foreach (var value in array)
                CollectSettings(value, result);
        }
    }

    private static JsonObject? FindSettingNode(JsonNode? node, string settingName)
    {
        if (node is JsonObject obj)
        {
            if (string.Equals(NodeText(obj["@prompt"]), settingName, StringComparison.OrdinalIgnoreCase) &&
                obj["@current"] is not null && obj["option"] is JsonArray)
                return obj;

            foreach (var value in obj.ToArray())
            {
                var match = FindSettingNode(value.Value, settingName);
                if (match is not null) return match;
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var value in array)
            {
                var match = FindSettingNode(value, settingName);
                if (match is not null) return match;
            }
        }
        return null;
    }

    private sealed record AsusOption(JsonNode? Node, string Value, string Prompt);

    private static IReadOnlyList<AsusOption> ReadOptions(JsonObject node)
    {
        if (node["option"] is not JsonArray options) return Array.Empty<AsusOption>();
        return options.OfType<JsonObject>()
            .Select(option => new AsusOption(
                option["#text"],
                NodeText(option["#text"]) ?? "",
                NodeText(option["@prompt"]) ?? ""))
            .Where(option => option.Value.Length > 0 && option.Prompt.Length > 0)
            .ToArray();
    }

    private static JsonObject? SelectEnabledOption(JsonObject setting, string requirement)
    {
        var options = (setting["option"] as JsonArray)?.OfType<JsonObject>().ToArray()
            ?? Array.Empty<JsonObject>();
        var preferred = requirement == "cpu-boost"
            ? new[] { "Enabled", "Enable", "Auto", "On", "Active" }
            : new[] { "Enabled", "Enable", "On", "Active" };

        foreach (var label in preferred)
        {
            var match = options.FirstOrDefault(option =>
                string.Equals(NodeText(option["@prompt"]), label, StringComparison.OrdinalIgnoreCase));
            if (match is not null && match["#text"] is not null)
                return match;
        }
        return null;
    }

    private static bool IsAlreadyEnabled(ParsedSetting setting, string requirement, string targetValue)
    {
        if (string.Equals(setting.CurrentValue, targetValue, StringComparison.OrdinalIgnoreCase))
            return true;

        var currentOption = ReadOptions(setting.Node).FirstOrDefault(option =>
            string.Equals(option.Value, setting.CurrentValue, StringComparison.OrdinalIgnoreCase));
        if (currentOption is null) return false;
        if (requirement == "cpu-boost" &&
            string.Equals(currentOption.Prompt, "Auto", StringComparison.OrdinalIgnoreCase))
            return true;
        return currentOption.Prompt.Equals("Enabled", StringComparison.OrdinalIgnoreCase) ||
            currentOption.Prompt.Equals("Enable", StringComparison.OrdinalIgnoreCase) ||
            currentOption.Prompt.Equals("On", StringComparison.OrdinalIgnoreCase) ||
            currentOption.Prompt.Equals("Active", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NodeText(JsonNode? node) => node is JsonValue value
        ? value.ToString()
        : null;

    private static void ResolvePendingJournal(IReadOnlyList<ParsedSetting> currentSettings)
    {
        if (!File.Exists(JournalPath)) return;
        try
        {
            var journal = JsonSerializer.Deserialize<AsusFirmwareJournal>(
                File.ReadAllText(JournalPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (journal is null || journal.Changes.Count == 0 || !File.Exists(journal.OriginalConfigPath))
                return;

            var allTarget = true;
            var allOriginal = true;
            foreach (var change in journal.Changes)
            {
                var current = currentSettings.FirstOrDefault(x =>
                    string.Equals(x.Name, change.Name, StringComparison.OrdinalIgnoreCase));
                if (current is null)
                {
                    allTarget = false;
                    allOriginal = false;
                    break;
                }
                allTarget &= string.Equals(current.CurrentValue, change.TargetValue, StringComparison.OrdinalIgnoreCase);
                allOriginal &= string.Equals(current.CurrentValue, change.OriginalValue, StringComparison.OrdinalIgnoreCase);
            }

            if (allTarget || allOriginal)
            {
                TryDelete(JournalPath);
                TryDelete(journal.OriginalConfigPath);
                AppLog.Write(allTarget
                    ? "ASUS BIOS confirmou as alterações depois da reinicialização."
                    : "ASUS BIOS permaneceu nos valores originais; a tentativa anterior não foi aplicada.");
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("Journal ASUS BIOS não pôde ser reconciliado: " + ex.Message);
        }
    }

    private static int RunAct(string executable, IReadOnlyList<string> arguments, int timeoutMilliseconds)
    {
        var isAdmin = false;
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            isAdmin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                Verb = isAdmin ? "" : "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            }
        };
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        try
        {
            process.Start();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException("A elevação UAC foi cancelada; a BIOS não foi alterada.", ex);
        }

        if (!process.WaitForExit(timeoutMilliseconds))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("O ASUS Configuration Tool excedeu o tempo limite.");
        }
        return process.ExitCode;
    }

    private static void WriteAtomic(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, overwrite: true);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
