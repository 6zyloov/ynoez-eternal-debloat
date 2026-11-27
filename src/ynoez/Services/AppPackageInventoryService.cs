using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ynoez.Services;

public sealed record AppxPackageInfo(
    string Name, string PackageFullName, string Version, string Publisher,
    string Status, bool IsFramework, bool IsResourcePackage, bool IsBundle,
    bool NonRemovable, string Classification, string InstallLocation);

public static class AppPackageInventoryService
{
    private static readonly string[] ProtectedNames =
    {
        "Microsoft.WindowsStore",
        "Microsoft.StorePurchaseApp",
        "Microsoft.DesktopAppInstaller",
        "Microsoft.WindowsCalculator",
        "Microsoft.WindowsNotepad",
        "MicrosoftWindows.Client.CBS",
        "Microsoft.Windows.Search",
        "Microsoft.Windows.ShellExperienceHost",
        "Microsoft.Windows.StartMenuExperienceHost",
        "Microsoft.SecHealthUI"
    };

    public static async Task<IReadOnlyList<AppxPackageInfo>> GetCurrentUserPackagesAsync(CancellationToken token)
    {
        const string script =
            "$ErrorActionPreference='Stop'; " +
            "$items=@(Get-AppxPackage -PackageTypeFilter Main,Framework,Resource,Bundle,Xap,Optional -ErrorAction Stop | Select-Object Name,PackageFullName,Version,Publisher,Status,IsFramework,IsResourcePackage,IsBundle,NonRemovable,InstallLocation); " +
            "ConvertTo-Json -InputObject $items -Depth 4 -Compress";
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

        process.Start();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        using var registration = timeout.Token.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch { }
        });
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("A consulta dos pacotes AppX excedeu 45 segundos.");
        }
        token.ThrowIfCancellationRequested();

        var output = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0)
            throw new InvalidOperationException("O Windows não conseguiu listar os pacotes AppX do usuário atual: " + error.Trim());
        return ParseJson(output);
    }

    internal static IReadOnlyList<AppxPackageInfo> ParseJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<AppxPackageInfo>();
        using var document = JsonDocument.Parse(json.Trim().TrimStart('\uFEFF'));
        var root = document.RootElement;
        var elements = root.ValueKind switch
        {
            JsonValueKind.Array => root.EnumerateArray().ToArray(),
            JsonValueKind.Object => new[] { root },
            JsonValueKind.Null => Array.Empty<JsonElement>(),
            _ => throw new InvalidDataException("Formato da resposta AppX inválido.")
        };

        return elements.Select(item =>
        {
            var name = GetString(item, "Name");
            var fullName = GetString(item, "PackageFullName");
            var framework = GetBool(item, "IsFramework");
            var resource = GetBool(item, "IsResourcePackage");
            var bundle = GetBool(item, "IsBundle");
            var nonRemovable = GetBool(item, "NonRemovable");
            var classification = nonRemovable || framework || resource || bundle || IsProtected(name)
                ? "Protegido / componente do Windows"
                : "Inventário — remoção não avaliada";
            return new AppxPackageInfo(name, fullName, GetString(item, "Version"),
                GetString(item, "Publisher"), GetString(item, "Status"), framework,
                resource, bundle, nonRemovable, classification, GetString(item, "InstallLocation"));
        })
        .Where(x => !string.IsNullOrWhiteSpace(x.Name))
        .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    }

    public static string ExportCsv(IReadOnlyList<AppxPackageInfo> packages)
    {
        Directory.CreateDirectory(AppPaths.Logs);
        var path = Path.Combine(AppPaths.Logs,
            "appx-inventory-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..8] + ".csv");
        File.WriteAllText(path, BuildCsv(packages), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        AppLog.Write("Inventário AppX exportado: " + path + " (" + packages.Count + " pacotes).");
        return path;
    }

    internal static string BuildCsv(IReadOnlyList<AppxPackageInfo> packages)
    {
        var lines = new List<string>
        {
            "Name,PackageFullName,Version,Publisher,Status,IsFramework,IsResourcePackage,IsBundle,NonRemovable,Classification,InstallLocation"
        };
        lines.AddRange(packages.Select(x => string.Join(",",
            Csv(x.Name), Csv(x.PackageFullName), Csv(x.Version), Csv(x.Publisher), Csv(x.Status),
            x.IsFramework, x.IsResourcePackage, x.IsBundle, x.NonRemovable, Csv(x.Classification), Csv(x.InstallLocation))));
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static bool IsProtected(string name) =>
        ProtectedNames.Contains(name, StringComparer.OrdinalIgnoreCase);

    private static string GetString(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return "";
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
    }

    private static bool GetBool(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) &&
        (value.ValueKind == JsonValueKind.True ||
         (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed) && parsed));

    private static string Csv(string? value)
    {
        value ??= string.Empty;
        var first = value.FirstOrDefault(c => !char.IsWhiteSpace(c));
        if (first is '=' or '+' or '-' or '@' || first == (char)9 || first == (char)13)
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
