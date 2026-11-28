using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ynoez.Services;

public sealed record NetworkAdapterPerformanceTweak(
    string AdapterName,
    string InterfaceDescription,
    string LinkSpeed,
    string DisplayName,
    string RegistryKeyword,
    string CurrentDisplayValue,
    string TargetDisplayValue,
    string CurrentRegistryValue,
    string TargetRegistryValue,
    bool IsAtTarget,
    bool HasBackup,
    bool HasConflict);

internal sealed record NetworkAdapterAdvancedPropertySnapshot(
    string AdapterName,
    string InterfaceDescription,
    string LinkSpeed,
    string DisplayName,
    string RegistryKeyword,
    string DisplayValue,
    string RegistryValue,
    List<string> ValidDisplayValues,
    List<string> ValidRegistryValues);

internal sealed record NetworkAdapterPerformanceBackup(
    string AdapterName,
    string RegistryKeyword,
    string DisplayName,
    string OriginalDisplayValue,
    string OriginalRegistryValue,
    string AppliedDisplayValue,
    DateTimeOffset CapturedAt);

/// <summary>
/// Exposes only three driver-reported properties: RSS (enabled), Interrupt Moderation
/// (disabled for a latency A/B test), and Energy Efficient Ethernet (disabled for
/// a link-latency A/B test). The driver's own valid display/registry value mapping
/// is used; no guessed registry number is sent to the adapter.
/// </summary>
public static class NetworkAdapterPerformanceTweakService
{
    private sealed record Target(string Keyword, string TargetDisplay, string Label, string Description);

    private static readonly Target[] Targets =
    [
        new("*RSS", "Enabled", "Receive Side Scaling (RSS)",
            "Distribui o processamento de recepção entre núcleos quando o driver suporta. Mantém habilitado para evitar concentrar todo o tráfego em um núcleo."),
        new("*InterruptModeration", "Disabled", "Interrupt Moderation",
            "Reduz a agregação de interrupções para testar latência menor. Pode aumentar uso de CPU; compare ping e frametime A/B."),
        new("*EEE", "Disabled", "Energy Efficient Ethernet",
            "Desativa economia de energia Ethernet quando o driver oferece a opção. Pode aumentar consumo; use apenas para testar pausas/renegociação do link."),
        new("*EnergyEfficientEthernet", "Disabled", "Energy Efficient Ethernet",
            "Desativa economia de energia Ethernet quando o driver oferece a opção. Pode aumentar consumo; use apenas para testar pausas/renegociação do link.")
    ];

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly string BackupDirectory = Path.Combine(AppPaths.Data, "NetworkAdvancedTweaks");

    private sealed class RawSnapshot
    {
        public List<NetworkAdapterAdvancedPropertySnapshot>? Properties { get; set; }
    }

    public static async Task<IReadOnlyList<NetworkAdapterPerformanceTweak>> GetCandidatesAsync(
        CancellationToken token = default)
    {
        var snapshot = await CaptureAsync(token);
        var result = new List<NetworkAdapterPerformanceTweak>();

        foreach (var property in snapshot)
        {
            var target = Targets.FirstOrDefault(x =>
                string.Equals(x.Keyword, property.RegistryKeyword, StringComparison.OrdinalIgnoreCase));
            if (target is null) continue;

            var targetIndex = FindChoiceIndex(property.ValidDisplayValues, target.TargetDisplay);
            if (targetIndex < 0 || targetIndex >= property.ValidRegistryValues.Count)
                continue;

            var targetDisplay = property.ValidDisplayValues[targetIndex];
            var targetRegistry = property.ValidRegistryValues[targetIndex];
            if (property.ValidDisplayValues.Count != property.ValidRegistryValues.Count ||
                property.ValidDisplayValues.Count == 0 ||
                property.CurrentDisplayValue.Contains(';') ||
                property.CurrentRegistryValue.Contains(';'))
                continue;

            var backupPath = GetBackupPath(property.AdapterName, property.RegistryKeyword);
            var hasBackup = File.Exists(backupPath);
            var atTarget = string.Equals(property.CurrentDisplayValue, targetDisplay, StringComparison.OrdinalIgnoreCase);
            var conflict = false;
            if (hasBackup)
            {
                var backup = ReadBackup(backupPath);
                conflict = !string.Equals(property.CurrentDisplayValue, backup.AppliedDisplayValue, StringComparison.OrdinalIgnoreCase);
            }

            result.Add(new NetworkAdapterPerformanceTweak(
                property.AdapterName, property.InterfaceDescription, property.LinkSpeed,
                target.Label, property.RegistryKeyword, property.CurrentDisplayValue,
                targetDisplay, property.CurrentRegistryValue, targetRegistry,
                atTarget, hasBackup, conflict));
        }

        return result
            .OrderBy(x => x.AdapterName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static async Task ApplyAsync(
        NetworkAdapterPerformanceTweak candidate,
        CancellationToken token = default)
    {
        ValidateCandidate(candidate);
        if (candidate.HasBackup)
            throw new InvalidOperationException("Este ajuste já possui backup. Restaure-o antes de aplicar novamente.");
        if (candidate.HasConflict)
            throw new InvalidOperationException("A propriedade foi alterada fora do ynoez; a alteração foi bloqueada.");
        if (candidate.IsAtTarget)
            throw new InvalidOperationException("O driver já apresenta o valor recomendado. Nenhuma alteração foi aplicada.");

        var current = (await CaptureAsync(token)).FirstOrDefault(x =>
            string.Equals(x.AdapterName, candidate.AdapterName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.RegistryKeyword, candidate.RegistryKeyword, StringComparison.OrdinalIgnoreCase));
        if (current is null ||
            !string.Equals(current.DisplayValue, candidate.CurrentDisplayValue, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(current.CurrentRegistryValue, candidate.CurrentRegistryValue, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A propriedade mudou desde a análise. Atualize a página antes de aplicar.");

        var path = GetBackupPath(candidate.AdapterName, candidate.RegistryKeyword);
        if (File.Exists(path))
            throw new InvalidOperationException("Já existe um backup para esta propriedade.");

        var backup = new NetworkAdapterPerformanceBackup(
            candidate.AdapterName, candidate.RegistryKeyword, candidate.DisplayName,
            current.DisplayValue, current.RegistryValue, candidate.TargetDisplayValue, DateTimeOffset.UtcNow);
        WriteAtomic(path, JsonSerializer.Serialize(backup, JsonOptions));

        try
        {
            await RunElevatedAsync(BuildSetScript(candidate.AdapterName, candidate.RegistryKeyword, candidate.TargetDisplayValue), token);
            var after = (await CaptureAsync(token)).FirstOrDefault(x =>
                string.Equals(x.AdapterName, candidate.AdapterName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.RegistryKeyword, candidate.RegistryKeyword, StringComparison.OrdinalIgnoreCase));
            if (after is null || !string.Equals(after.DisplayValue, candidate.TargetDisplayValue, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("O driver não confirmou o valor solicitado.");
            AppLog.Write("Ajuste de rede aplicado: " + candidate.AdapterName + " / " + candidate.RegistryKeyword + ".");
        }
        catch (OperationCanceledException)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
            throw;
        }
        catch
        {
            try { await RunElevatedAsync(BuildSetScript(candidate.AdapterName, candidate.RegistryKeyword, current.DisplayValue), CancellationToken.None); }
            catch (Exception rollbackError)
            {
                AppLog.Write("Reversão de NIC falhou; backup preservado: " + rollbackError);
                throw;
            }
            try { if (File.Exists(path)) File.Delete(path); } catch { }
            throw;
        }
    }

    public static async Task RestoreAsync(
        NetworkAdapterPerformanceTweak candidate,
        CancellationToken token = default)
    {
        ValidateCandidate(candidate);
        var path = GetBackupPath(candidate.AdapterName, candidate.RegistryKeyword);
        if (!File.Exists(path))
            throw new InvalidOperationException("Não existe backup original para este ajuste.");

        var backup = ReadBackup(path);
        if (!string.Equals(backup.AdapterName, candidate.AdapterName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(backup.RegistryKeyword, candidate.RegistryKeyword, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(backup.DisplayName, candidate.DisplayName, StringComparison.Ordinal))
            throw new InvalidDataException("O backup não corresponde à propriedade de rede selecionada.");

        var current = (await CaptureAsync(token)).FirstOrDefault(x =>
            string.Equals(x.AdapterName, candidate.AdapterName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.RegistryKeyword, candidate.RegistryKeyword, StringComparison.OrdinalIgnoreCase));
        if (current is null)
            throw new InvalidOperationException("O adaptador ou propriedade não está disponível. O backup foi preservado.");
        if (!string.Equals(current.DisplayValue, backup.AppliedDisplayValue, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A propriedade mudou fora do ynoez após a aplicação. Para não sobrescrever outro ajuste, o backup foi preservado.");

        await RunElevatedAsync(BuildSetScript(candidate.AdapterName, candidate.RegistryKeyword, backup.OriginalDisplayValue), token);
        var after = (await CaptureAsync(token)).FirstOrDefault(x =>
            string.Equals(x.AdapterName, candidate.AdapterName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.RegistryKeyword, candidate.RegistryKeyword, StringComparison.OrdinalIgnoreCase));
        if (after is null || !string.Equals(after.DisplayValue, backup.OriginalDisplayValue, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("O valor original não foi confirmado. O backup foi preservado.");

        File.Delete(path);
        AppLog.Write("Ajuste de rede restaurado: " + candidate.AdapterName + " / " + candidate.RegistryKeyword + ".");
    }

    internal static int FindChoiceIndex(IReadOnlyList<string> values, string requested)
    {
        var expected = Normalize(requested);
        for (var index = 0; index < values.Count; index++)
        {
            if (Normalize(values[index]) == expected)
                return index;
        }

        var synonyms = expected switch
        {
            "enabled" => new[] { "enabled", "on", "ativado", "habilitado", "ligado" },
            "disabled" => new[] { "disabled", "off", "desativado", "desabilitado", "desligado" },
            _ => new[] { expected }
        };
        for (var index = 0; index < values.Count; index++)
            if (synonyms.Contains(Normalize(values[index]), StringComparer.Ordinal))
                return index;
        return -1;
    }

    internal static string BuildSetScript(string adapterName, string registryKeyword, string displayValue)
    {
        ValidateText(adapterName, nameof(adapterName));
        ValidateText(registryKeyword, nameof(registryKeyword));
        ValidateText(displayValue, nameof(displayValue));
        var n = Escape(adapterName);
        var k = Escape(registryKeyword);
        var v = Escape(displayValue);
        return "$ErrorActionPreference='Stop'; " +
            "$n='" + n + "'; $k='" + k + "'; $v='" + v + "'; " +
            "$p=Get-NetAdapterAdvancedProperty -Name $n -AllProperties -RegistryKeyword $k -ErrorAction Stop; " +
            "if ($null -eq $p) { throw 'Propriedade não encontrada' }; " +
            "$valid=@($p.ValidDisplayValues | ForEach-Object { [string]$_ }); " +
            "if ($valid -notcontains $v) { throw 'Valor não permitido pelo driver' }; " +
            "Set-NetAdapterAdvancedProperty -Name $n -RegistryKeyword $k -DisplayValue $v -NoRestart -ErrorAction Stop | Out-Null; " +
            "$after=Get-NetAdapterAdvancedProperty -Name $n -AllProperties -RegistryKeyword $k -ErrorAction Stop; " +
            "if ([string]$after.DisplayValue -ne $v) { throw 'Verificação de NIC falhou' }; exit 0";
    }

    private static async Task<List<NetworkAdapterAdvancedPropertySnapshot>> CaptureAsync(CancellationToken token)
    {
        Directory.CreateDirectory(AppPaths.Data);
        var outputPath = Path.Combine(AppPaths.Data, "network-performance-" + Guid.NewGuid().ToString("N") + ".json");
        var script = BuildCaptureScript(outputPath);
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(AppPaths.PowerShellExecutable,
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        try
        {
            process.Start();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));
            using var registration = timeout.Token.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            });
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new TimeoutException("A consulta de propriedades de NIC excedeu 25 segundos.");
            }
            token.ThrowIfCancellationRequested();
            var output = await stdout;
            var error = await stderr;
            if (process.ExitCode != 0)
                throw new InvalidOperationException("A consulta de propriedades avançadas falhou: " + error.Trim() + " " + output.Trim());
            if (!File.Exists(outputPath))
                throw new InvalidDataException("O Windows não gerou o inventário das propriedades avançadas de rede.");
            var result = JsonSerializer.Deserialize<RawSnapshot>(
                (await File.ReadAllTextAsync(outputPath, token)).Trim().TrimStart('\uFEFF'), JsonOptions);
            return result?.Properties ?? [];
        }
        finally
        {
            try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
        }
    }

    private static string BuildCaptureScript(string outputPath)
    {
        var path = Escape(outputPath);
        return "$ErrorActionPreference='Stop'; $items=@(); " +
            "foreach($a in @(Get-NetAdapter -Physical -ErrorAction Stop)) { " +
            "try { foreach($p in @(Get-NetAdapterAdvancedProperty -Name $a.Name -AllProperties -ErrorAction Stop)) { " +
            "$items += [pscustomobject]@{AdapterName=[string]$a.Name;InterfaceDescription=[string]$a.InterfaceDescription;LinkSpeed=[string]$a.LinkSpeed;DisplayName=[string]$p.DisplayName;RegistryKeyword=[string]$p.RegistryKeyword;DisplayValue=[string]$p.DisplayValue;RegistryValue=[string]$p.RegistryValue;ValidDisplayValues=@($p.ValidDisplayValues | ForEach-Object {[string]$_});ValidRegistryValues=@($p.ValidRegistryValues | ForEach-Object {[string]$_})} " +
            "} } catch {} }; " +
            "[pscustomobject]@{Properties=@($items)} | ConvertTo-Json -Depth 8 -Compress | Set-Content -LiteralPath '" + path + "' -Encoding UTF8; exit 0";
    }

    private static async Task RunElevatedAsync(string script, CancellationToken token)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var isAdmin = false;
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            isAdmin = new System.Security.Principal.WindowsPrincipal(identity)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(AppPaths.PowerShellExecutable,
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded)
            {
                UseShellExecute = true,
                Verb = isAdmin ? "" : "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            }
        };

        try { process.Start(); }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException("A elevação para alterar a NIC foi cancelada.", ex, token);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(35));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            if (token.IsCancellationRequested) throw;
            throw new TimeoutException("A alteração avançada da NIC excedeu 35 segundos.");
        }
        if (process.ExitCode != 0)
            throw new InvalidOperationException("O Windows recusou a alteração de NIC (código " + process.ExitCode + ").");
    }

    private static NetworkAdapterPerformanceBackup ReadBackup(string path) =>
        JsonSerializer.Deserialize<NetworkAdapterPerformanceBackup>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidDataException("Backup de propriedade de NIC inválido.");

    private static string GetBackupPath(string adapterName, string keyword)
    {
        ValidateText(adapterName, nameof(adapterName));
        ValidateText(keyword, nameof(keyword));
        Directory.CreateDirectory(BackupDirectory);
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            adapterName.ToUpperInvariant() + "|" + keyword.ToUpperInvariant()))).ToLowerInvariant();
        return Path.Combine(BackupDirectory, id + ".json");
    }

    private static void WriteAtomic(string path, string content)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, overwrite: true);
    }

    private static void ValidateCandidate(NetworkAdapterPerformanceTweak candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!Targets.Any(x =>
            string.Equals(x.Keyword, candidate.RegistryKeyword, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Label, candidate.DisplayName, StringComparison.Ordinal)) ||
            string.IsNullOrWhiteSpace(candidate.AdapterName) ||
            string.IsNullOrWhiteSpace(candidate.TargetDisplayValue))
            throw new InvalidOperationException("A propriedade não pertence à lista de ajustes de NIC permitidos.");
    }

    private static void ValidateText(string value, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(char.IsControl) || value.Contains('''))
            throw new ArgumentException("Valor inválido para comando de NIC.", parameter);
    }

    private static string Normalize(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        foreach (var character in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToLowerInvariant(character));
        return new string(builder.ToString().Where(char.IsLetterOrDigit).ToArray());
    }

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
