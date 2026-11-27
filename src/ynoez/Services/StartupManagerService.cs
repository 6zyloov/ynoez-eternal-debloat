using Microsoft.Win32;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ynoez.Services;

public sealed record StartupEntry(
    string Name, string Command, string Scope, string KeyPath, bool CanDisable, string ProtectionReason, string Id);

public sealed record DisabledStartupEntry(
    string Name, string Command, string KeyPath, string ValueName, string BackupId, DateTimeOffset DisabledAt);

public sealed class StartupManagerService
{
    private const string UserRunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string MachineRunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static readonly string[] ProtectedTerms =
    {
        "securityhealth", "defender", "windows security", "nvidia", "nvcontainer", "nvdisplay",
        "vanguard", "riot", "faceit", "easyanticheat", "battleye", "amd", "realtek",
        "audio", "synaptics", "touchpad", "intel", "bluetooth", "wireless", "wlan"
    };
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _stateDirectory;
    private readonly string _manifestPath;
    private readonly RegistryTweakService _registry;

    public StartupManagerService(string? stateDirectory = null)
    {
        _stateDirectory = stateDirectory ?? Path.Combine(AppPaths.Data, "Startup");
        Directory.CreateDirectory(_stateDirectory);
        _manifestPath = Path.Combine(_stateDirectory, "disabled-startup.json");
        _registry = new RegistryTweakService(Path.Combine(_stateDirectory, "RegistryRestore"));
    }

    public IReadOnlyList<StartupEntry> GetActiveEntries()
    {
        var entries = new List<StartupEntry>();
        using (var user = Registry.CurrentUser.OpenSubKey(UserRunPath, writable: false))
        {
            if (user is not null)
                foreach (var name in user.GetValueNames())
                {
                    var command = Convert.ToString(user.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)) ?? "";
                    var id = MakeId(UserRunPath, name);
                    var protectedReason = GetProtectionReason(name, command);
                    entries.Add(new StartupEntry(name, command, "Usuário atual", UserRunPath,
                        protectedReason.Length == 0, protectedReason, id));
                }
        }

        ReadMachineEntries(RegistryView.Registry64, "Sistema (64 bits)", entries);
        ReadMachineEntries(RegistryView.Registry32, "Sistema (32 bits)", entries);
        return entries.OrderBy(x => x.Scope, StringComparer.Ordinal).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public IReadOnlyList<DisabledStartupEntry> GetDisabledEntries()
    {
        if (!File.Exists(_manifestPath)) return Array.Empty<DisabledStartupEntry>();
        try
        {
            return JsonSerializer.Deserialize<List<DisabledStartupEntry>>(File.ReadAllText(_manifestPath), JsonOptions)
                ?? new List<DisabledStartupEntry>();
        }
        catch (Exception ex)
        {
            AppLog.Write("Manifesto de inicialização desativada inválido: " + ex.Message);
            return Array.Empty<DisabledStartupEntry>();
        }
    }

    public void Disable(StartupEntry entry)
    {
        if (!entry.CanDisable || entry.Scope != "Usuário atual" || entry.KeyPath != UserRunPath)
            throw new InvalidOperationException("Somente entradas não protegidas de inicialização do usuário atual podem ser desativadas.");
        if (string.IsNullOrWhiteSpace(entry.Name) || entry.Id != MakeId(UserRunPath, entry.Name))
            throw new InvalidOperationException("Identidade da entrada de inicialização inválida.");

        using (var currentKey = Registry.CurrentUser.OpenSubKey(UserRunPath, writable: false))
        {
            var currentCommand = currentKey?.GetValue(entry.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (currentCommand is null || !string.Equals(Convert.ToString(currentCommand), entry.Command, StringComparison.Ordinal))
                throw new InvalidOperationException("A entrada mudou desde a leitura. Atualize a página antes de tentar desativá-la.");
        }

        var tweak = MakeTweak(entry.Name, entry.Command, entry.Id);
        _registry.RemoveValue(tweak);

        var entries = GetDisabledEntries().ToList();
        if (!entries.Any(x => x.BackupId == entry.Id))
            entries.Add(new DisabledStartupEntry(entry.Name, entry.Command, entry.KeyPath, entry.Name, entry.Id, DateTimeOffset.UtcNow));
        try
        {
            WriteManifest(entries);
        }
        catch
        {
            try { _registry.Restore(tweak); }
            catch (Exception rollbackError) { AppLog.Write("Falha ao reverter desativação de inicialização após erro no manifesto: " + rollbackError); }
            throw;
        }
        AppLog.Write("Inicialização desativada com backup: " + entry.Name);
    }

    public void Restore(DisabledStartupEntry entry)
    {
        if (entry.KeyPath != UserRunPath || string.IsNullOrWhiteSpace(entry.ValueName))
            throw new InvalidOperationException("Manifesto de inicialização inválido.");
        var tweak = MakeTweak(entry.ValueName, entry.Command, entry.BackupId);
        _registry.Restore(tweak);
        try
        {
            WriteManifest(GetDisabledEntries().Where(x => x.BackupId != entry.BackupId).ToList());
        }
        catch
        {
            try { _registry.RemoveValue(tweak); }
            catch (Exception rollbackError) { AppLog.Write("Falha ao reverter restauração de inicialização após erro no manifesto: " + rollbackError); }
            throw;
        }
        AppLog.Write("Inicialização restaurada: " + entry.Name);
    }

    private void ReadMachineEntries(RegistryView view, string scope, List<StartupEntry> entries)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = baseKey.OpenSubKey(MachineRunPath, writable: false);
            if (key is null) return;
            foreach (var name in key.GetValueNames())
            {
                var command = Convert.ToString(key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)) ?? "";
                entries.Add(new StartupEntry(name, command, scope, MachineRunPath, false,
                    "Entrada de máquina: consulta somente nesta versão.", MakeId(scope, name)));
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("Não foi possível consultar inicialização " + scope + ": " + ex.Message);
        }
    }

    private static string GetProtectionReason(string name, string command)
    {
        var text = (name + " " + command).ToLowerInvariant();
        var term = ProtectedTerms.FirstOrDefault(text.Contains);
        return term is null ? "" : "Protegido por possível dependência de driver, segurança, GPU ou anti-cheat (" + term + ").";
    }

    private static string MakeId(string path, string name)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(path + "|" + name.ToUpperInvariant()));
        return "startup-" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static RegistryTweak MakeTweak(string name, string command, string id) =>
        new(id, name, "Entrada de inicialização", "Inicialização", "Médio",
            RegistryHive.CurrentUser, UserRunPath, name, command, RegistryValueKind.String);

    private void WriteManifest(List<DisabledStartupEntry> entries)
    {
        var temp = _manifestPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(entries, JsonOptions));
        File.Move(temp, _manifestPath, overwrite: true);
    }
}
