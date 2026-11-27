using Microsoft.Win32;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ynoez.Services;

public sealed record StartupEntry(
    string Name, string Command, string Scope, string KeyPath, bool CanDisable, string ProtectionReason, string Id,
    string? FilePath = null);

public sealed record DisabledStartupEntry(
    string Name, string Command, string KeyPath, string ValueName, string BackupId, DateTimeOffset DisabledAt,
    bool IsFile = false, string? OriginalFilePath = null, string? BackupFilePath = null);

public sealed class StartupManagerService
{
    private const string UserRunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string MachineRunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string UserStartupScope = "Pasta de inicialização (usuário atual)";
    private static readonly string[] ProtectedTerms =
    {
        "securityhealth", "defender", "windows security", "nvidia", "nvcontainer", "nvdisplay",
        "vanguard", "riot", "faceit", "easyanticheat", "battleye", "amd", "realtek",
        "audio", "synaptics", "touchpad", "intel", "bluetooth", "wireless", "wlan",
        "roblox", "download"
    };
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _stateDirectory;
    private readonly string _manifestPath;
    private readonly string _disabledFilesDirectory;
    private readonly string _userStartupDirectory;
    private readonly RegistryTweakService _registry;

    public StartupManagerService(string? stateDirectory = null, string? userStartupDirectory = null)
    {
        _stateDirectory = stateDirectory ?? Path.Combine(AppPaths.Data, "Startup");
        Directory.CreateDirectory(_stateDirectory);
        _manifestPath = Path.Combine(_stateDirectory, "disabled-startup.json");
        _disabledFilesDirectory = Path.Combine(_stateDirectory, "DisabledFiles");
        Directory.CreateDirectory(_disabledFilesDirectory);
        var startup = userStartupDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        if (string.IsNullOrWhiteSpace(startup))
            startup = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Windows", "Start Menu", "Programs", "Startup");
        _userStartupDirectory = Path.GetFullPath(startup);
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

        ReadStartupFolder(_userStartupDirectory, UserStartupScope, canDisable: true, entries);
        var commonStartup = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
        if (!string.IsNullOrWhiteSpace(commonStartup))
            ReadStartupFolder(commonStartup, "Pasta de inicialização (todos usuários)", canDisable: false, entries);

        ReadMachineEntries(RegistryView.Registry64, "Sistema (64 bits)", entries);
        ReadMachineEntries(RegistryView.Registry32, "Sistema (32 bits)", entries);
        return entries.OrderBy(x => x.Scope, StringComparer.Ordinal)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
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
        if (entry.FilePath is not null)
        {
            DisableStartupFile(entry);
            return;
        }

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

    private void DisableStartupFile(StartupEntry entry)
    {
        if (!entry.CanDisable || entry.Scope != UserStartupScope || string.IsNullOrWhiteSpace(entry.FilePath))
            throw new InvalidOperationException("Somente arquivos não protegidos da pasta de inicialização do usuário atual podem ser desativados.");
        var originalPath = ValidateUserStartupPath(entry.FilePath, entry.Name);
        var id = MakeId(_userStartupDirectory, entry.Name);
        if (!string.Equals(entry.KeyPath, _userStartupDirectory, StringComparison.OrdinalIgnoreCase) || entry.Id != id)
            throw new InvalidOperationException("Identidade do arquivo de inicialização mudou. Atualize a página.");
        if (!File.Exists(originalPath))
            throw new FileNotFoundException("O arquivo de inicialização não existe mais.", originalPath);

        var backupPath = Path.Combine(_disabledFilesDirectory, id + Path.GetExtension(originalPath));
        if (File.Exists(backupPath))
            throw new InvalidOperationException("Já existe um backup para este arquivo de inicialização.");
        Directory.CreateDirectory(_disabledFilesDirectory);
        File.Move(originalPath, backupPath);

        var entries = GetDisabledEntries().ToList();
        entries.Add(new DisabledStartupEntry(entry.Name, originalPath, _userStartupDirectory, entry.Name, id,
            DateTimeOffset.UtcNow, true, originalPath, backupPath));
        try { WriteManifest(entries); }
        catch
        {
            try { File.Move(backupPath, originalPath); }
            catch (Exception rollbackError) { AppLog.Write("Falha ao devolver arquivo de inicialização após erro no manifesto: " + rollbackError); }
            throw;
        }
        AppLog.Write("Arquivo da pasta de inicialização movido para backup reversível: " + entry.Name);
    }

    public void Restore(DisabledStartupEntry entry)
    {
        if (entry.IsFile)
        {
            RestoreStartupFile(entry);
            return;
        }

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

    private void RestoreStartupFile(DisabledStartupEntry entry)
    {
        if (!string.Equals(entry.KeyPath, _userStartupDirectory, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(entry.OriginalFilePath) || string.IsNullOrWhiteSpace(entry.BackupFilePath)
            || string.IsNullOrWhiteSpace(entry.BackupId))
            throw new InvalidDataException("Manifesto de arquivo de inicialização inválido.");
        var originalPath = ValidateUserStartupPath(entry.OriginalFilePath, entry.ValueName);
        var expectedId = MakeId(_userStartupDirectory, entry.ValueName);
        if (!string.Equals(entry.BackupId, expectedId, StringComparison.Ordinal))
            throw new InvalidDataException("ID de backup de arquivo de inicialização inválido.");
        var expectedBackup = Path.Combine(_disabledFilesDirectory, expectedId + Path.GetExtension(originalPath));
        var backupPath = ValidateBackupFilePath(entry.BackupFilePath);
        if (!string.Equals(backupPath, expectedBackup, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("O backup não corresponde ao arquivo de inicialização.");
        if (!File.Exists(backupPath))
            throw new FileNotFoundException("O backup do arquivo de inicialização não foi encontrado.", backupPath);
        if (File.Exists(originalPath))
            throw new IOException("Já existe um arquivo com esse nome na pasta de inicialização. O backup foi mantido para evitar sobrescrita.");

        File.Move(backupPath, originalPath);
        try { WriteManifest(GetDisabledEntries().Where(x => x.BackupId != entry.BackupId).ToList()); }
        catch
        {
            try { File.Move(originalPath, backupPath); }
            catch (Exception rollbackError) { AppLog.Write("Falha ao devolver arquivo para a pasta de backup após erro no manifesto: " + rollbackError); }
            throw;
        }
        AppLog.Write("Arquivo da pasta de inicialização restaurado: " + entry.Name);
    }

    private void ReadStartupFolder(string folder, string scope, bool canDisable, List<StartupEntry> entries)
    {
        try
        {
            var fullFolder = Path.GetFullPath(folder);
            if (!Directory.Exists(fullFolder)) return;
            foreach (var file in Directory.EnumerateFiles(fullFolder, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(file);
                var protectedReason = GetProtectionReason(name, file);
                if (!canDisable && protectedReason.Length == 0)
                    protectedReason = "Pasta de inicialização compartilhada: somente leitura nesta versão.";
                var allowed = canDisable && protectedReason.Length == 0;
                entries.Add(new StartupEntry(name, file, scope, fullFolder, allowed, protectedReason,
                    MakeId(fullFolder, name), file));
            }
        }
        catch (Exception ex) { AppLog.Write("Não foi possível consultar a pasta de inicialização '" + folder + "': " + ex.Message); }
    }

    private string ValidateUserStartupPath(string filePath, string expectedName)
    {
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(expectedName))
            throw new InvalidDataException("Caminho ou nome de arquivo vazio.");
        var full = Path.GetFullPath(filePath);
        var parent = Path.GetDirectoryName(full);
        if (!string.Equals(parent, _userStartupDirectory, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetFileName(full), expectedName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("O arquivo não pertence diretamente à pasta de inicialização atual.");
        return full;
    }

    private string ValidateBackupFilePath(string filePath)
    {
        var full = Path.GetFullPath(filePath);
        var root = Path.GetFullPath(_disabledFilesDirectory) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Caminho de backup fora da pasta interna do ynoez.");
        return full;
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
        if (term is not null) return "Protegido por possível dependência de driver, segurança, GPU, jogo ou download (" + term + ").";
        if (System.Text.RegularExpressions.Regex.IsMatch(text, @"(^|[^a-z0-9])xt([^a-z0-9]|$)"))
            return "Protegido por corresponder a XT/script pessoal.";
        return "";
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
