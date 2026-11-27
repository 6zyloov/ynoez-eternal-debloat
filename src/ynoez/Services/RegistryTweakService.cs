using System.IO;
using Microsoft.Win32;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ynoez.Services;

/// <summary>
/// Transactional, per-user registry tweak engine. Only catalogued HKCU values are writable.
/// Original state is persisted before mutation; restoration is conservative and idempotent.
/// </summary>
public sealed class RegistryTweakService
{
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _stateDirectory;

    public RegistryTweakService(string? stateDirectory = null)
    {
        _stateDirectory = stateDirectory ?? Path.Combine(AppPaths.Data, "RegistryRestore");
        Directory.CreateDirectory(_stateDirectory);
    }

    public TweakState Inspect(RegistryTweak tweak)
    {
        Validate(tweak);
        using var key = Registry.CurrentUser.OpenSubKey(tweak.KeyPath, writable: false);
        if (key is null || !key.GetValueNames().Contains(tweak.ValueName, StringComparer.OrdinalIgnoreCase))
            return TweakState.NotConfigured;
        var current = key.GetValue(tweak.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return ValuesEqual(current, tweak.Value, tweak.Kind) ? TweakState.Enabled : TweakState.Disabled;
    }

    public void Apply(RegistryTweak tweak)
    {
        Validate(tweak);
        lock (Sync)
        {
            var backupPath = GetBackupPath(tweak.Id);
            if (!File.Exists(backupPath))
                WriteAtomically(backupPath, JsonSerializer.Serialize(Capture(tweak), JsonOptions));

            using var key = Registry.CurrentUser.CreateSubKey(tweak.KeyPath, writable: true)
                ?? throw new InvalidOperationException("O Windows não permitiu abrir a chave de registro.");
            key.SetValue(tweak.ValueName, tweak.Value, tweak.Kind);
            key.Flush();

            if (Inspect(tweak) != TweakState.Enabled)
                throw new InvalidOperationException("A verificação posterior à alteração falhou.");
            AppLog.Write($"Tweak aplicado: {tweak.Id}");
        }
    }

    public void RemoveValue(RegistryTweak tweak)
    {
        Validate(tweak);
        lock (Sync)
        {
            var backupPath = GetBackupPath(tweak.Id);
            if (!File.Exists(backupPath))
                WriteAtomically(backupPath, JsonSerializer.Serialize(Capture(tweak), JsonOptions));

            using var key = Registry.CurrentUser.OpenSubKey(tweak.KeyPath, writable: true);
            if (key is null)
                throw new InvalidOperationException("A chave de inicialização não existe mais.");
            key.DeleteValue(tweak.ValueName, throwOnMissingValue: false);
            key.Flush();

            if (key.GetValueNames().Contains(tweak.ValueName, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("Não foi possível remover a entrada de inicialização.");
            AppLog.Write("Entrada de inicialização removida: " + tweak.Id);
        }
    }

    public bool CanRestore(string id) => File.Exists(GetBackupPath(id));

    public void Restore(RegistryTweak tweak)
    {
        Validate(tweak);
        lock (Sync)
        {
            var backupPath = GetBackupPath(tweak.Id);
            if (!File.Exists(backupPath))
                throw new InvalidOperationException("Não existe backup original para este ajuste.");

            var backup = JsonSerializer.Deserialize<RegistryValueBackup>(File.ReadAllText(backupPath), JsonOptions)
                ?? throw new InvalidDataException("Backup de registro inválido.");
            if (!string.Equals(backup.Id, tweak.Id, StringComparison.Ordinal))
                throw new InvalidDataException("O ID do backup não corresponde ao ajuste.");

            using (var key = Registry.CurrentUser.OpenSubKey(tweak.KeyPath, writable: true))
            {
                if (backup.ValueExisted)
                {
                    if (key is null)
                        throw new InvalidOperationException("A chave original não existe; restauração interrompida.");
                    key.SetValue(tweak.ValueName, DecodeOriginalValue(backup), backup.OriginalKind);
                    key.Flush();
                }
                else if (key is not null)
                {
                    key.DeleteValue(tweak.ValueName, throwOnMissingValue: false);
                    key.Flush();
                }
            }

            if (backup.CreatedKey && !backup.ValueExisted)
            {
                try { Registry.CurrentUser.DeleteSubKey(tweak.KeyPath, throwOnMissingSubKey: false); }
                catch (ArgumentException) { /* Preserve non-empty keys. */ }
            }

            using (var verifyKey = Registry.CurrentUser.OpenSubKey(tweak.KeyPath, writable: false))
            {
                var restoredExists = verifyKey is not null &&
                    verifyKey.GetValueNames().Contains(tweak.ValueName, StringComparer.OrdinalIgnoreCase);
                if (restoredExists != backup.ValueExisted)
                    throw new InvalidOperationException("A verificação da restauração falhou; o backup foi mantido.");
                if (backup.ValueExisted)
                {
                    var restored = verifyKey!.GetValue(tweak.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    if (!OriginalValuesEqual(restored, DecodeOriginalValue(backup)))
                        throw new InvalidOperationException("O valor restaurado não corresponde ao backup; o backup foi mantido.");
                }
            }

            File.Delete(backupPath);
            AppLog.Write($"Tweak restaurado: {tweak.Id}");
        }
    }

    private RegistryValueBackup Capture(RegistryTweak tweak)
    {
        using var key = Registry.CurrentUser.OpenSubKey(tweak.KeyPath, writable: false);
        var exists = key is not null && key.GetValueNames().Contains(tweak.ValueName, StringComparer.OrdinalIgnoreCase);
        object? value = exists ? key!.GetValue(tweak.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) : null;
        var kind = exists ? key!.GetValueKind(tweak.ValueName) : RegistryValueKind.String;
        return new RegistryValueBackup(tweak.Id, exists, value, kind, key is null, DateTimeOffset.UtcNow);
    }

    private string GetBackupPath(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new ArgumentException("ID de ajuste inválido.", nameof(id));
        return Path.Combine(_stateDirectory, id + ".json");
    }

    private static object DecodeOriginalValue(RegistryValueBackup backup)
    {
        if (backup.OriginalValue is not JsonElement element)
            return backup.OriginalValue ?? string.Empty;
        return backup.OriginalKind switch
        {
            RegistryValueKind.DWord => element.GetInt32(),
            RegistryValueKind.QWord => element.GetInt64(),
            RegistryValueKind.String or RegistryValueKind.ExpandString => element.GetString() ?? string.Empty,
            RegistryValueKind.Binary or RegistryValueKind.None => element.GetBytesFromBase64(),
            RegistryValueKind.MultiString => element.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToArray(),
            _ => throw new InvalidDataException("Tipo original de registro não suportado.")
        };
    }

    private static void Validate(RegistryTweak tweak)
    {
        if (tweak.Hive != RegistryHive.CurrentUser)
            throw new InvalidOperationException("O motor aceita somente HKCU nesta versão.");
        if (tweak.MinimumBuild > 0 && GetWindowsBuild() < tweak.MinimumBuild)
            throw new InvalidOperationException("Este ajuste exige Windows build " + tweak.MinimumBuild + " ou superior.");
        var path = tweak.KeyPath.Replace('/', '\\').Trim('\\');
        var allowed = new[] { "Software\\Microsoft\\Windows\\CurrentVersion\\", "Software\\Microsoft\\GameBar\\" };
        if (!allowed.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Caminho de registro fora da lista permitida.");
        if (path.Contains("..", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(tweak.ValueName))
            throw new InvalidOperationException("Caminho ou nome de valor inválido.");
        if (tweak.Kind is not RegistryValueKind.DWord and not RegistryValueKind.String)
            throw new InvalidOperationException("Tipo de registro não suportado.");
        if (tweak.Kind == RegistryValueKind.DWord && tweak.Value is not int)
            throw new InvalidOperationException("Valor DWORD precisa ser inteiro.");
        if (tweak.Kind == RegistryValueKind.String && tweak.Value is not string)
            throw new InvalidOperationException("Valor REG_SZ precisa ser texto.");
    }

    private static int GetWindowsBuild()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion");
            return int.TryParse(Convert.ToString(key?.GetValue("CurrentBuildNumber")), out var build) ? build : 0;
        }
        catch { return 0; }
    }

    private static bool OriginalValuesEqual(object? left, object? right)
    {
        if (left is byte[] leftBytes && right is byte[] rightBytes)
            return leftBytes.SequenceEqual(rightBytes);
        if (left is string[] leftStrings && right is string[] rightStrings)
            return leftStrings.SequenceEqual(rightStrings, StringComparer.Ordinal);
        return Equals(left, right);
    }

    private static bool ValuesEqual(object? left, object? right, RegistryValueKind kind) =>
        kind == RegistryValueKind.DWord
            ? Convert.ToInt32(left) == Convert.ToInt32(right)
            : string.Equals(Convert.ToString(left), Convert.ToString(right), StringComparison.Ordinal);

    private static void WriteAtomically(string path, string content)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, overwrite: true);
    }
}

public sealed record RegistryTweak(
    string Id, string Name, string Description, string Category, string Risk,
    RegistryHive Hive, string KeyPath, string ValueName, object Value, RegistryValueKind Kind, int MinimumBuild = 0);

public sealed record RegistryValueBackup(
    string Id, bool ValueExisted, object? OriginalValue, RegistryValueKind OriginalKind,
    bool CreatedKey, DateTimeOffset CapturedAt);

public enum TweakState { NotConfigured, Enabled, Disabled }
