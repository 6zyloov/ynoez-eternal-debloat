using Microsoft.Win32;
using System.Globalization;
using System.IO;
using System.Text;

namespace ynoez.Services;

public sealed record InstalledProgramInfo(
    string Name, string Version, string Publisher, string InstallDate,
    string Scope, string Architecture, string Classification);

public static class InstalledProgramInventoryService
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    public static IReadOnlyList<InstalledProgramInfo> GetInstalledPrograms(CancellationToken token)
    {
        var result = new List<InstalledProgramInfo>();
        var views = Environment.Is64BitOperatingSystem
            ? new[] { RegistryView.Registry64, RegistryView.Registry32 }
            : new[] { RegistryView.Default };

        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in views)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var uninstall = baseKey.OpenSubKey(UninstallPath, writable: false);
                    if (uninstall is null) continue;

                    foreach (var subKeyName in uninstall.GetSubKeyNames())
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            using var item = uninstall.OpenSubKey(subKeyName, writable: false);
                            if (item is null) continue;
                            var name = ReadString(item, "DisplayName");
                            if (string.IsNullOrWhiteSpace(name)) continue;

                            var publisher = ReadString(item, "Publisher");
                            var version = ReadString(item, "DisplayVersion");
                            var installDate = FormatInstallDate(ReadString(item, "InstallDate"));
                            var releaseType = ReadString(item, "ReleaseType");
                            var parent = ReadString(item, "ParentKeyName");
                            var systemComponent = ReadDword(item, "SystemComponent") == 1;
                            var scope = hive == RegistryHive.LocalMachine ? "Computador" : "Usuário atual";
                            var architecture = view == RegistryView.Registry64 ? "64 bits"
                                : view == RegistryView.Registry32 ? "32 bits" : "Padrão";

                            result.Add(new InstalledProgramInfo(
                                name.Trim(), version, publisher, installDate, scope, architecture,
                                Classify(name, publisher, releaseType, systemComponent, parent)));
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            AppLog.Write("Entrada de programa ignorada (" + subKeyName + "): " + ex.Message);
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    AppLog.Write("Inventário de programas indisponível (" + hive + "/" + view + "): " + ex.Message);
                }
            }
        }

        return result
            .OrderBy(x => x.Classification, StringComparer.CurrentCulture)
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Version, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string ExportCsv(IReadOnlyList<InstalledProgramInfo> programs)
    {
        Directory.CreateDirectory(AppPaths.Logs);
        var path = Path.Combine(AppPaths.Logs,
            "installed-programs-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) +
            "-" + Guid.NewGuid().ToString("N")[..8] + ".csv");

        using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        writer.WriteLine("Nome,Versão,Editor,Data de instalação,Escopo,Arquitetura,Classificação");
        foreach (var item in programs)
        {
            writer.WriteLine(string.Join(",",
                EscapeCsv(item.Name), EscapeCsv(item.Version), EscapeCsv(item.Publisher),
                EscapeCsv(item.InstallDate), EscapeCsv(item.Scope), EscapeCsv(item.Architecture),
                EscapeCsv(item.Classification)));
        }
        return path;
    }

    internal static string Classify(
        string name, string publisher, string releaseType, bool systemComponent, string parentKeyName)
    {
        if (systemComponent)
            return "Protegido — componente do Windows/oculto";

        if (!string.IsNullOrWhiteSpace(parentKeyName))
            return "Protegido — componente vinculado a outro pacote";

        if (ContainsAny(releaseType, "update", "hotfix", "security", "service pack", "rollup", "patch"))
            return "Protegido — atualização/correção";

        if (ContainsAny(name, ProtectedNameFragments) ||
            ContainsAny(name, "update for", "security update", "hotfix for", "service pack for") ||
            ContainsAny(publisher, "nvidia", "advanced micro devices", "realtek semiconductor", "intel corporation"))
            return "Protegido — driver/runtime/anti-cheat";

        return "Aplicativo desktop — revisar antes de desinstalar";
    }

    internal static string EscapeCsv(string? value)
    {
        value ??= string.Empty;
        var first = value.FirstOrDefault(c => !char.IsWhiteSpace(c));
        if (first is '=' or '+' or '-' or '@' or '\t' or '\r')
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static bool ContainsAny(string? value, params string[] fragments)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return fragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static string ReadString(RegistryKey key, string name)
    {
        try
        {
            return Convert.ToString(key.GetValue(name, string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames),
                CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
        }
        catch { return string.Empty; }
    }

    private static int ReadDword(RegistryKey key, string name)
    {
        try { return Convert.ToInt32(key.GetValue(name, 0), CultureInfo.InvariantCulture); }
        catch { return 0; }
    }

    private static string FormatInstallDate(string value)
    {
        if (value.Length != 8 || !DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date))
            return value;
        return date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    }
}
