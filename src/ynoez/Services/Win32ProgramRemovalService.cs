using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace ynoez.Services;

public sealed record Win32UninstallCandidate(
    string DisplayName,
    string Version,
    string Publisher,
    string HiveName,
    RegistryView View,
    string RegistrySubKey,
    string UninstallCommand,
    bool WindowsInstaller,
    string? ProductCode,
    string? ExecutablePath,
    string Arguments,
    bool IsProtected,
    string ProtectionReason,
    bool CanUninstall);

public sealed record Win32UninstallLaunchResult(
    string DisplayName,
    string ExecutablePath,
    string Arguments,
    DateTimeOffset LaunchedAt);

/// <summary>
/// Safe launcher for registered Win32 uninstallers. MSI records are normalized
/// to msiexec /x {ProductCode}; non-MSI records must resolve to a real executable.
/// Script hosts and command interpreters are never launched.
/// </summary>
public static class Win32ProgramRemovalService
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private static readonly RegistryView[] Views =
        Environment.Is64BitOperatingSystem
            ? [RegistryView.Registry64, RegistryView.Registry32]
            : [RegistryView.Default];

    private static readonly string[] ProtectedFragments =
    [
        "visual c++", ".net runtime", ".net desktop runtime", "windows desktop runtime",
        "webview2 runtime", "nvidia", "amd chipset", "advanced micro devices", "realtek",
        "intel chipset", "riot vanguard", "faceit anti-cheat", "easy anti-cheat",
        "battleye", "windows driver package", "microsoft .net", "microsoft edge webview"
    ];

    private static readonly HashSet<string> BlockedExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd.exe", "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe",
        "rundll32.exe", "regsvr32.exe", "mshta.exe", "installutil.exe",
        "schtasks.exe", "sc.exe", "reg.exe", "wmic.exe"
    };

    public static IReadOnlyList<Win32UninstallCandidate> GetCandidates()
    {
        var result = new List<Win32UninstallCandidate>();
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in Views)
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var uninstall = baseKey.OpenSubKey(UninstallPath, writable: false);
                    if (uninstall is null) continue;

                    foreach (var subKeyName in uninstall.GetSubKeyNames())
                    {
                        try
                        {
                            using var item = uninstall.OpenSubKey(subKeyName, writable: false);
                            if (item is null) continue;
                            var displayName = ReadString(item, "DisplayName");
                            if (string.IsNullOrWhiteSpace(displayName)) continue;

                            var publisher = ReadString(item, "Publisher");
                            var version = ReadString(item, "DisplayVersion");
                            var command = ReadString(item, "UninstallString");
                            var windowsInstaller = ReadDword(item, "WindowsInstaller") == 1;
                            var systemComponent = ReadDword(item, "SystemComponent") == 1;
                            var releaseType = ReadString(item, "ReleaseType");
                            var parentKey = ReadString(item, "ParentKeyName");
                            var protectedReason = GetProtectionReason(
                                displayName, publisher, systemComponent, releaseType, parentKey);
                            var invocation = BuildInvocation(subKeyName, windowsInstaller, command);

                            result.Add(new Win32UninstallCandidate(
                                displayName.Trim(), version, publisher,
                                hive == RegistryHive.LocalMachine ? "Computador" : "Usuário atual",
                                view, subKeyName, command, windowsInstaller,
                                invocation?.ProductCode, invocation?.ExecutablePath,
                                invocation?.Arguments ?? "", protectedReason.Length > 0,
                                protectedReason, protectedReason.Length == 0 && invocation is not null));
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            AppLog.Write("Entrada Win32 ignorada (" + subKeyName + "): " + ex.Message);
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Write("Leitura de desinstalação indisponível (" + hive + "/" + view + "): " + ex.Message);
                }
            }
        }

        return result
            .Where(x => x.CanUninstall)
            .GroupBy(x => (x.DisplayName, x.Version, x.HiveName), StringTupleComparer.Instance)
            .Select(group => group.First())
            .OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public static Win32UninstallLaunchResult LaunchUninstaller(Win32UninstallCandidate candidate)
    {
        ValidateCurrentEntry(candidate);
        var invocation = BuildInvocation(
            candidate.RegistrySubKey, candidate.WindowsInstaller, candidate.UninstallCommand)
            ?? throw new InvalidOperationException("O comando de desinstalação não é executável de forma segura.");

        var executable = invocation.ExecutablePath;
        var arguments = invocation.Arguments;
        if (invocation.ProductCode is not null)
        {
            arguments = "/x " + invocation.ProductCode;
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable, arguments)
            {
                UseShellExecute = true,
                Verb = candidate.HiveName == "Computador" && !IsAdministrator() ? "runas" : "",
                WorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory
            }
        };

        try
        {
            if (!process.Start())
                throw new InvalidOperationException("O Windows não iniciou o desinstalador.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException("A elevação foi cancelada. O desinstalador não foi iniciado.", ex);
        }

        var result = new Win32UninstallLaunchResult(
            candidate.DisplayName, executable, arguments, DateTimeOffset.UtcNow);
        AppLog.Write("Desinstalador Win32 iniciado pelo usuário: " +
            candidate.DisplayName + " • " + executable + " " + arguments);
        return result;
    }

    internal static (string ExecutablePath, string Arguments, string? ProductCode)? BuildInvocation(
        string registrySubKey, bool windowsInstaller, string uninstallCommand)
    {
        if (string.IsNullOrWhiteSpace(registrySubKey) || string.IsNullOrWhiteSpace(uninstallCommand))
            return null;

        var productCode = FindProductCode(uninstallCommand) ?? FindProductCode(registrySubKey);
        if (windowsInstaller && productCode is not null)
        {
            var msi = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System), "msiexec.exe");
            return File.Exists(msi) ? (msi, "/x " + productCode, productCode) : null;
        }

        var command = Environment.ExpandEnvironmentVariables(uninstallCommand.Trim());
        string executable;
        string arguments;

        if (command.StartsWith('"'))
        {
            var closing = command.IndexOf('"', 1);
            if (closing <= 1) return null;
            executable = command[1..closing];
            arguments = command[(closing + 1)..].Trim();
        }
        else
        {
            var match = Regex.Match(command, @"^(?<exe>.+?\.exe)(?<args>\s.*)?$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success) return null;
            executable = match.Groups["exe"].Value.Trim();
            arguments = match.Groups["args"].Value.Trim();
        }

        if (!Path.IsPathFullyQualified(executable)) return null;
        try { executable = Path.GetFullPath(executable); }
        catch { return null; }

        if (!File.Exists(executable) ||
            BlockedExecutables.Contains(Path.GetFileName(executable)))
            return null;

        return (executable, arguments, null);
    }

    private static void ValidateCurrentEntry(Win32UninstallCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!candidate.CanUninstall || candidate.IsProtected ||
            (candidate.HiveName != "Computador" && candidate.HiveName != "Usuário atual") ||
            string.IsNullOrWhiteSpace(candidate.RegistrySubKey))
            throw new InvalidOperationException("Este programa não é elegível para desinstalação pelo ynoez.");

        var hive = candidate.HiveName == "Computador" ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
        using var baseKey = RegistryKey.OpenBaseKey(hive, candidate.View);
        using var uninstall = baseKey.OpenSubKey(UninstallPath, writable: false);
        using var item = uninstall?.OpenSubKey(candidate.RegistrySubKey, writable: false);
        if (item is null ||
            !string.Equals(ReadString(item, "DisplayName"), candidate.DisplayName, StringComparison.Ordinal) ||
            !string.Equals(ReadString(item, "UninstallString"), candidate.UninstallCommand, StringComparison.Ordinal))
            throw new InvalidOperationException("A entrada mudou depois da análise. Atualize a lista antes de continuar.");

        var currentReason = GetProtectionReason(
            candidate.DisplayName, ReadString(item, "Publisher"),
            ReadDword(item, "SystemComponent") == 1,
            ReadString(item, "ReleaseType"), ReadString(item, "ParentKeyName"));
        if (currentReason.Length > 0)
            throw new InvalidOperationException("A proteção do programa mudou: " + currentReason);
        if (BuildInvocation(candidate.RegistrySubKey, ReadDword(item, "WindowsInstaller") == 1,
            ReadString(item, "UninstallString")) is null)
            throw new InvalidOperationException("O comando de desinstalação não passou pela validação de segurança.");
    }

    private static string GetProtectionReason(
        string name, string publisher, bool systemComponent, string releaseType, string parentKey)
    {
        if (systemComponent) return "Componente do sistema";
        if (!string.IsNullOrWhiteSpace(releaseType)) return "Atualização ou componente de manutenção";
        if (!string.IsNullOrWhiteSpace(parentKey)) return "Componente filho de outro produto";
        var combined = (name + " " + publisher).ToLowerInvariant();
        var match = ProtectedFragments.FirstOrDefault(fragment => combined.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        return match is null ? "" : "Driver, runtime ou proteção: " + match;
    }

    private static string? FindProductCode(string value)
    {
        var match = Regex.Match(value, @"\{[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}\}");
        return match.Success && Guid.TryParse(match.Value, out var parsed)
            ? "{" + parsed.ToString().ToUpperInvariant() + "}"
            : null;
    }

    private static string ReadString(RegistryKey key, string name) =>
        Convert.ToString(key.GetValue(name, "")) ?? "";

    private static int ReadDword(RegistryKey key, string name)
    {
        try { return Convert.ToInt32(key.GetValue(name, 0)); }
        catch { return 0; }
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

    private sealed class StringTupleComparer : IEqualityComparer<(string DisplayName, string Version, string HiveName)>
    {
        public static readonly StringTupleComparer Instance = new();
        public bool Equals((string DisplayName, string Version, string HiveName) x,
            (string DisplayName, string Version, string HiveName) y) =>
            string.Equals(x.DisplayName, y.DisplayName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Version, y.Version, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.HiveName, y.HiveName, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string DisplayName, string Version, string HiveName) value) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.DisplayName),
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.Version),
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.HiveName));
    }
}
