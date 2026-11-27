using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ynoez.Services;

public sealed record AppxSystemPackageInfo(
    string Name, string PackageName, string Version, string Architecture,
    string Scope, bool IsFramework, bool IsResourcePackage, bool IsBundle,
    bool NonRemovable, string Classification);

public static class AppxSystemInventoryService
{
    private static readonly string[] ProtectedNames =
    {
        "Microsoft.WindowsStore", "Microsoft.StorePurchaseApp", "Microsoft.DesktopAppInstaller",
        "Microsoft.WindowsCalculator", "Microsoft.WindowsNotepad", "Microsoft.Windows.Search",
        "MicrosoftWindows.Client.CBS", "Microsoft.Windows.ShellExperienceHost",
        "Microsoft.Windows.StartMenuExperienceHost", "Microsoft.SecHealthUI"
    };

    public static async Task<IReadOnlyList<AppxSystemPackageInfo>> GetAllUsersAndProvisionedAsync(CancellationToken token)
    {
        Directory.CreateDirectory(AppPaths.Data);
        var outputPath = Path.Combine(AppPaths.Data, "appx-system-" + Guid.NewGuid().ToString("N") + ".json");
        var safePath = outputPath.Replace("'", "''", StringComparison.Ordinal);
        var script =
            "$ErrorActionPreference='Stop'; Import-Module Appx -ErrorAction Stop; Import-Module Dism -ErrorAction Stop; " +
            "$installed=@(Get-AppxPackage -AllUsers -PackageTypeFilter Main,Framework,Resource,Bundle,Xap,Optional | " +
                "Select-Object @{N='Name';E={$_.Name}},@{N='PackageName';E={$_.PackageFullName}},Version,Architecture," +
                "@{N='Scope';E={'Todos os usuários'}},IsFramework,IsResourcePackage,IsBundle,NonRemovable); " +
            "$provisioned=@(Get-AppxProvisionedPackage -Online | " +
                "Select-Object @{N='Name';E={$_.DisplayName}},@{N='PackageName';E={$_.PackageName}},Version,Architecture," +
                "@{N='Scope';E={'Provisionado'}},@{N='IsFramework';E={$false}},@{N='IsResourcePackage';E={$false}}," +
                "@{N='IsBundle';E={$false}},@{N='NonRemovable';E={$false}}); " +
            "$items=@($installed)+@($provisioned); ConvertTo-Json -InputObject @($items) -Depth 4 -Compress | " +
                "Set-Content -LiteralPath '" + safePath + "' -Encoding UTF8";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var isAdmin = IsAdministrator();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("powershell.exe",
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded)
            {
                UseShellExecute = true,
                Verb = isAdmin ? "" : "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            }
        };

        try
        {
            process.Start();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(90));
            using var registration = timeout.Token.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch { }
            });
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new TimeoutException("A consulta dos pacotes AppX de sistema excedeu 90 segundos.");
            }
            token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0)
                throw new InvalidOperationException("O PowerShell administrativo não conseguiu consultar AppX e pacotes provisionados (código " + process.ExitCode + ").");
            if (!File.Exists(outputPath))
                throw new InvalidDataException("O Windows não criou o relatório AppX esperado.");
            return ParseJson(await File.ReadAllTextAsync(outputPath, token));
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException("A elevação foi cancelada pelo usuário. Nenhum pacote foi alterado.", ex, token);
        }
        finally
        {
            try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
        }
    }

    internal static IReadOnlyList<AppxSystemPackageInfo> ParseJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<AppxSystemPackageInfo>();
        using var document = JsonDocument.Parse(json.Trim().TrimStart('\uFEFF'));
        var root = document.RootElement;
        var elements = root.ValueKind switch
        {
            JsonValueKind.Array => root.EnumerateArray().ToArray(),
            JsonValueKind.Object => new[] { root },
            JsonValueKind.Null => Array.Empty<JsonElement>(),
            _ => throw new InvalidDataException("Formato do inventário AppX de sistema inválido.")
        };

        return elements.Select(item =>
        {
            var name = GetString(item, "Name");
            var scope = GetString(item, "Scope");
            var framework = GetBool(item, "IsFramework");
            var resource = GetBool(item, "IsResourcePackage");
            var bundle = GetBool(item, "IsBundle");
            var nonRemovable = GetBool(item, "NonRemovable");
            var protectedPackage = nonRemovable || framework || resource || bundle ||
                ProtectedNames.Contains(name, StringComparer.OrdinalIgnoreCase);
            var classification = protectedPackage
                ? "Protegido / componente do Windows"
                : scope == "Provisionado"
                    ? "Provisionado — remoção não avaliada"
                    : "Pacote de usuário — remoção não avaliada";
            return new AppxSystemPackageInfo(name, GetString(item, "PackageName"),
                GetString(item, "Version"), GetString(item, "Architecture"), scope,
                framework, resource, bundle, nonRemovable, classification);
        })
        .Where(x => !string.IsNullOrWhiteSpace(x.Name))
        .OrderBy(x => x.Scope, StringComparer.OrdinalIgnoreCase)
        .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    }

    public static string ExportCsv(IReadOnlyList<AppxSystemPackageInfo> packages)
    {
        Directory.CreateDirectory(AppPaths.Logs);
        var path = Path.Combine(AppPaths.Logs, "appx-system-" +
            DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..8] + ".csv");
        File.WriteAllText(path, BuildCsv(packages), new UTF8Encoding(true));
        AppLog.Write("Inventário AppX de sistema exportado: " + path);
        return path;
    }

    internal static string BuildCsv(IReadOnlyList<AppxSystemPackageInfo> packages)
    {
        var lines = new List<string> { "Name,PackageName,Version,Architecture,Scope,IsFramework,IsResourcePackage,IsBundle,NonRemovable,Classification" };
        lines.AddRange(packages.Select(x => string.Join(",",
            EscapeCsv(x.Name), EscapeCsv(x.PackageName), EscapeCsv(x.Version), EscapeCsv(x.Architecture),
            EscapeCsv(x.Scope), x.IsFramework, x.IsResourcePackage, x.IsBundle, x.NonRemovable, EscapeCsv(x.Classification))));
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    internal static string EscapeCsv(string? value)
    {
        value ??= string.Empty;
        var first = value.FirstOrDefault(c => !char.IsWhiteSpace(c));
        if (first is '=' or '+' or '-' or '@' || first == (char)9 || first == (char)13)
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static bool IsAdministrator()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private static string GetString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? (value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()) ?? ""
            : "";

    private static bool GetBool(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) &&
        (value.ValueKind == JsonValueKind.True ||
         (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed) && parsed));
}
