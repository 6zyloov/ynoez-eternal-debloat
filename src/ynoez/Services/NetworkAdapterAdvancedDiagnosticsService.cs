using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ynoez.Services;

public sealed record NetworkAdapterAdvancedProperty(
    string AdapterName,
    string AdapterDescription,
    string DisplayName,
    string DisplayValue,
    string RegistryKeyword,
    string RegistryValue,
    string Category,
    string Assessment,
    string Guidance);

public sealed record NetworkAdapterAdvancedDiagnosticsSnapshot(
    DateTimeOffset CapturedAt,
    IReadOnlyList<NetworkAdapterAdvancedProperty> Properties,
    IReadOnlyList<string> Errors);

/// <summary>
/// Read-only inventory of advanced NIC driver properties. This service never writes
/// adapter properties; recommendations are explanatory and deliberately conditional.
/// </summary>
public static class NetworkAdapterAdvancedDiagnosticsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class RawSnapshot
    {
        public RawSnapshot() { }

        public DateTimeOffset CapturedAt { get; set; }
        public List<RawProperty>? Properties { get; set; }
        public List<string>? Errors { get; set; }
    }

    private sealed class RawProperty
    {
        public RawProperty() { }

        public string? AdapterName { get; set; }
        public string? AdapterDescription { get; set; }
        public string? DisplayName { get; set; }
        public string? DisplayValue { get; set; }
        public string? RegistryKeyword { get; set; }
        public string? RegistryValue { get; set; }
    }

    public static async Task<NetworkAdapterAdvancedDiagnosticsSnapshot> CaptureAsync(
        CancellationToken token = default)
    {
        Directory.CreateDirectory(AppPaths.Data);
        var outputPath = Path.Combine(AppPaths.Data, "network-advanced-" + Guid.NewGuid().ToString("N") + ".json");
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(BuildScript(outputPath)));

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(AppPaths.PowerShellExecutable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            }
        };
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-NonInteractive");
        process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
        process.StartInfo.ArgumentList.Add("Bypass");
        process.StartInfo.ArgumentList.Add("-EncodedCommand");
        process.StartInfo.ArgumentList.Add(encoded);

        try
        {
            token.ThrowIfCancellationRequested();
            if (!process.Start())
                throw new InvalidOperationException("Não foi possível iniciar a consulta de propriedades avançadas de rede.");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);

            try
            {
                await process.WaitForExitAsync(timeout.Token);
                _ = await stdoutTask;
                var error = await stderrTask;
                token.ThrowIfCancellationRequested();

                if (process.ExitCode != 0)
                    throw new InvalidOperationException("A consulta de propriedades avançadas falhou: " + error.Trim());
            }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                if (token.IsCancellationRequested) throw;
                throw new TimeoutException("A consulta de propriedades avançadas excedeu 25 segundos.");
            }

            if (!File.Exists(outputPath))
                throw new InvalidDataException("O Windows não gerou o relatório de propriedades avançadas.");

            var snapshot = ParseJson(File.ReadAllText(outputPath));
            AppLog.Write("Inventário somente leitura de propriedades avançadas de rede: " +
                snapshot.Properties.Count + " propriedades; " + snapshot.Errors.Count + " avisos.");
            return snapshot;
        }
        finally
        {
            try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
        }
    }

    internal static NetworkAdapterAdvancedDiagnosticsSnapshot ParseJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("A consulta de rede não retornou JSON.");

        var raw = JsonSerializer.Deserialize<RawSnapshot>(json.Trim().TrimStart('\uFEFF'), JsonOptions)
            ?? throw new InvalidDataException("O relatório de propriedades avançadas está vazio.");

        if (raw.CapturedAt == default)
            throw new InvalidDataException("O relatório não contém uma data de captura.");

        var properties = (raw.Properties ?? new List<RawProperty>())
            .Where(x => x is not null &&
                !string.IsNullOrWhiteSpace(x.AdapterName) &&
                (!string.IsNullOrWhiteSpace(x.DisplayName) ||
                 !string.IsNullOrWhiteSpace(x.RegistryKeyword)))
            .Select(x =>
            {
                var adapter = x.AdapterName!.Trim();
                var keyword = (x.RegistryKeyword ?? "").Trim();
                var name = string.IsNullOrWhiteSpace(x.DisplayName)
                    ? keyword + " (sem nome visível)"
                    : x.DisplayName.Trim();
                var value = (x.DisplayValue ?? "").Trim();
                var registryValue = (x.RegistryValue ?? "").Trim();
                var recommendation = Classify(name, keyword, value);
                return new NetworkAdapterAdvancedProperty(
                    adapter,
                    (x.AdapterDescription ?? "").Trim(),
                    name,
                    value.Length == 0 ? "—" : value,
                    keyword,
                    registryValue,
                    recommendation.Category,
                    recommendation.Assessment,
                    recommendation.Guidance);
            })
            .OrderBy(x => x.AdapterName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var errors = (raw.Errors ?? new List<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new NetworkAdapterAdvancedDiagnosticsSnapshot(raw.CapturedAt, properties, errors);
    }

    private static (string Category, string Assessment, string Guidance) Classify(
        string displayName, string registryKeyword, string displayValue)
    {
        var name = Normalize(displayName);
        var keyword = Normalize(registryKeyword);
        var value = Normalize(displayValue);
        var enabled = value is "enabled" or "on" or "true" or "1" or "ativado" or "ligado";
        var disabled = value is "disabled" or "off" or "false" or "0" or "desativado" or "desligado";

        if (name.Contains("interruptmoderation", StringComparison.Ordinal) ||
            keyword.Contains("interruptmoderation", StringComparison.Ordinal))
        {
            return ("Latência", "Trade-off entre latência e CPU",
                "A moderação agrupa interrupções e pode reduzir uso de CPU, mas altera a resposta da NIC. Não existe valor universal para jogos: compare ligado/desligado em A/B, na mesma cena, e acompanhe frametime, ping e uso de CPU.");
        }

        if (name.Contains("energyefficientethernet", StringComparison.Ordinal) ||
            name.Contains("greenethernet", StringComparison.Ordinal) ||
            keyword is "eee" or "energyefficientethernet" ||
            keyword.Contains("greenethernet", StringComparison.Ordinal))
        {
            return ("Energia", enabled
                ? "Economia de energia ativa"
                : disabled ? "Economia de energia desativada" : "Valor específico do driver",
                "EEE/Green Ethernet reduz consumo em determinadas condições de link. Não desative por rotina: investigue primeiro logs, cabo, porta e roteador; teste uma mudança reversível somente se houver indício de renegociação ou instabilidade.");
        }

        if (name.Contains("receivesidescaling", StringComparison.Ordinal) ||
            keyword is "rss" or "receivesidescaling")
        {
            return ("Escalonamento", enabled
                ? "Ativado — normalmente manter"
                : disabled ? "Desativado — verificar suporte" : "Valor específico do driver",
                "RSS distribui o processamento de recepção entre processadores lógicos compatíveis. Em geral, mantenha habilitado quando suportado; confirme o driver e a topologia antes de alterar.");
        }

        if (name.Contains("largesendoffload", StringComparison.Ordinal) ||
            keyword.StartsWith("lso", StringComparison.Ordinal))
        {
            return ("Offload", enabled
                ? "Ativado — normalmente manter"
                : disabled ? "Desativado — investigar necessidade" : "Valor específico do driver",
                "Large Send Offload transfere parte da segmentação TCP para a NIC. Em geral, mantenha o padrão do driver; só teste outra configuração para investigar uma incompatibilidade reproduzível e compare throughput, CPU e estabilidade.");
        }

        if (name.Contains("checksumoffload", StringComparison.Ordinal) ||
            keyword.Contains("checksumoffload", StringComparison.Ordinal))
        {
            return ("Offload", enabled
                ? "Ativado — normalmente manter"
                : disabled ? "Desativado — investigar necessidade" : "Valor específico do driver",
                "Checksum Offload reduz trabalho de cálculo da CPU usando a NIC. Normalmente deve permanecer no padrão habilitado do driver; não desligue sem evidência de incompatibilidade ou erro reproduzível.");
        }

        if (name.Contains("speedduplex", StringComparison.Ordinal) ||
            name.Contains("speedandduplex", StringComparison.Ordinal) ||
            keyword.Contains("speedduplex", StringComparison.Ordinal))
        {
            var automatic = value.Contains("auto", StringComparison.Ordinal) ||
                value.Contains("negotiation", StringComparison.Ordinal) ||
                value.Contains("automatic", StringComparison.Ordinal);
            return ("Link", automatic ? "Negociação automática" : "Velocidade/duplex fixos",
                automatic
                    ? "A negociação automática costuma ser a opção mais compatível. Confirme se a velocidade negociada corresponde à porta, ao cabo e ao roteador."
                    : "Uma velocidade ou duplex fixos podem causar incompatibilidade com a outra ponta do link. Só force um valor quando a configuração da porta correspondente e a documentação do fabricante exigirem isso.");
        }

        if (name.Contains("flowcontrol", StringComparison.Ordinal) ||
            keyword.Contains("flowcontrol", StringComparison.Ordinal))
        {
            return ("Fluxo", "Sem padrão universal",
                "Flow Control depende da NIC, do switch/roteador e do tipo de tráfego. Não altere apenas para jogos; investigue perda, pausas e compatibilidade da outra ponta antes de testar.");
        }

        if (name.Contains("receivebuffers", StringComparison.Ordinal) ||
            name.Contains("transmitbuffers", StringComparison.Ordinal) ||
            keyword.Contains("receivebuffers", StringComparison.Ordinal) ||
            keyword.Contains("transmitbuffers", StringComparison.Ordinal))
        {
            return ("Buffers", "Valor específico do driver",
                "O intervalo e o melhor valor variam por fabricante e memória disponível. Valores maiores não garantem menor latência; avalie erros/descartes, throughput e uso de memória antes de qualquer ajuste.");
        }

        return ("Outros", "Somente informativo",
            "Propriedade disponibilizada pelo driver. O ynoez não recomenda alteração automática sem documentação específica do fabricante e um teste reproduzível.");
    }

    private static string Normalize(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    public static string ExportCsv(NetworkAdapterAdvancedDiagnosticsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Directory.CreateDirectory(AppPaths.Logs);
        var path = Path.Combine(AppPaths.Logs,
            "network-advanced-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) +
            "-" + Guid.NewGuid().ToString("N")[..8] + ".csv");

        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine("CapturedAt,AdapterName,AdapterDescription,Category,DisplayName,DisplayValue,RegistryKeyword,RegistryValue,Assessment,Guidance");
        foreach (var item in snapshot.Properties)
        {
            writer.WriteLine(string.Join(",",
                EscapeCsv(snapshot.CapturedAt.ToString("O", CultureInfo.InvariantCulture)),
                EscapeCsv(item.AdapterName), EscapeCsv(item.AdapterDescription), EscapeCsv(item.Category),
                EscapeCsv(item.DisplayName), EscapeCsv(item.DisplayValue), EscapeCsv(item.RegistryKeyword),
                EscapeCsv(item.RegistryValue), EscapeCsv(item.Assessment), EscapeCsv(item.Guidance)));
        }

        AppLog.Write("Relatório CSV de propriedades avançadas de rede exportado: " + path);
        return path;
    }

    internal static string BuildScriptForTest(string outputPath) => BuildScript(outputPath);

    private static string BuildScript(string outputPath)
    {
        var path = EscapePowerShell(outputPath);
        return @"
$ErrorActionPreference = 'Stop'
$items = New-Object System.Collections.Generic.List[object]
$warnings = New-Object System.Collections.Generic.List[string]
try {
    $adapters = @(Get-NetAdapter -Physical -ErrorAction Stop)
    if ($adapters.Count -eq 0) {
        $warnings.Add('Nenhum adaptador físico foi encontrado.')
    }
    foreach ($adapter in $adapters) {
        try {
            $properties = @(Get-NetAdapterAdvancedProperty -Name $adapter.Name -AllProperties -ErrorAction Stop)
            foreach ($property in $properties) {
                $displayValue = (@($property.DisplayValue) | ForEach-Object { [string]$_ }) -join '; '
                $registryValue = (@($property.RegistryValue) | ForEach-Object { [string]$_ }) -join '; '
                $items.Add([pscustomobject]@{
                    AdapterName = [string]$adapter.Name
                    AdapterDescription = [string]$adapter.InterfaceDescription
                    DisplayName = [string]$property.DisplayName
                    DisplayValue = [string]$displayValue
                    RegistryKeyword = [string]$property.RegistryKeyword
                    RegistryValue = [string]$registryValue
                })
            }
        } catch {
            $errors.Add(('Adaptador {0}: {1}' -f $adapter.Name, $_.Exception.Message))
        }
    }
} catch {
    $errors.Add(('Falha ao enumerar adaptadores físicos: {0}' -f $_.Exception.Message))
}
$result = [pscustomobject]@{
    CapturedAt = [DateTimeOffset]::UtcNow.ToString('o')
    Properties = @($items.ToArray())
    Errors = @($warnings.ToArray())
}
ConvertTo-Json -InputObject $result -Depth 6 -Compress |
    Set-Content -LiteralPath '" + path + @"' -Encoding UTF8 -ErrorAction Stop
";
    }

    private static string EscapePowerShell(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    internal static string EscapeCsv(string? value)
    {
        value ??= string.Empty;
        var first = value.FirstOrDefault(c => !char.IsWhiteSpace(c));
        if (first is '=' or '+' or '-' or '@' || first == (char)9 || first == (char)13)
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
