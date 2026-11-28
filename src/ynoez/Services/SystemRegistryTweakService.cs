using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using System.Text.Json;

namespace ynoez.Services;

public sealed record SystemRegistryTweakDefinition(
    string Id, string Name, string Description, bool RequiresRestart);

public sealed record SystemRegistryTweakStatus(
    SystemRegistryTweakDefinition Definition, bool IsApplied, bool HasBackup, bool CanChange);

internal sealed record SystemRegistryValueDefinition(
    string KeyPath, string ValueName, RegistryValueKind Kind, int DwordValue = 0, string? StringValue = null);

internal sealed record SystemRegistryValueSnapshot(
    string KeyPath, string ValueName, bool KeyExisted, bool ValueExisted,
    RegistryValueKind Kind, int DwordValue, string? StringValue);

internal sealed record SystemRegistryBackup(
    string Id, List<SystemRegistryValueSnapshot> Values, DateTimeOffset CapturedAt);

/// <summary>
/// Elevated, allow-listed HKLM tweaks. Each group snapshots exact original values
/// before writing and keeps its journal until restoration is verified.
/// </summary>
public static class SystemRegistryTweakService
{
    private sealed record Definition(
        SystemRegistryTweakDefinition Public,
        IReadOnlyList<SystemRegistryValueDefinition> Values);

    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string BackupDirectory = Path.Combine(AppPaths.Data, "SystemRegistryRestore");

    private static readonly Definition[] Catalog =
    [
        new(
            new SystemRegistryTweakDefinition(
                "mmcss-games",
                "Prioridade MMCSS para jogos",
                "Ajusta a tarefa Games do Windows para prioridade média-alta. Só afeta jogos que usam MMCSS; não força prioridade em qualquer processo.",
                true),
            [
                new(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games",
                    "Priority", RegistryValueKind.DWord, 6),
                new(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games",
                    "Scheduling Category", RegistryValueKind.String, StringValue: "Medium")
            ]),
        new(
            new SystemRegistryTweakDefinition(
                "network-throttling",
                "Desativar throttling multimídia da rede",
                "Define NetworkThrottlingIndex como 0xFFFFFFFF. Ajuste legado de rede multimídia; pode não produzir efeito em versões recentes e precisa de teste A/B.",
                true),
            [
                new(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
                    "NetworkThrottlingIndex", RegistryValueKind.DWord, -1)
            ]),
        new(
            new SystemRegistryTweakDefinition(
                "gpu-scheduling",
                "Agendamento de GPU por hardware (HAGS)",
                "Solicita HAGS ao Windows. Depende de GPU, driver e versão do Windows; compare ligado/desligado. Reinicie para o Windows considerar a mudança.",
                true),
            [
                new(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers",
                    "HwSchMode", RegistryValueKind.DWord, 2)
            ])
    ];

    private static readonly IReadOnlyDictionary<string, Definition> ById =
        Catalog.ToDictionary(x => x.Public.Id, StringComparer.Ordinal);

    public static IReadOnlyList<SystemRegistryTweakDefinition> GetCatalog() =>
        Catalog.Select(x => x.Public).ToArray();

    public static SystemRegistryTweakStatus GetStatus(string id)
    {
        var definition = GetDefinition(id);
        var hasBackup = File.Exists(GetBackupPath(id));
        var applied = IsApplied(id);
        return new SystemRegistryTweakStatus(
            definition.Public,
            applied || hasBackup,
            hasBackup,
            !applied || hasBackup);
    }

    public static bool IsApplied(string id)
    {
        var definition = GetDefinition(id);
        return definition.Values.All(IsTargetValuePresent);
    }

    public static void Apply(string id)
    {
        lock (Sync)
        {
            var definition = GetDefinition(id);
            var backupPath = GetBackupPath(id);
            if (File.Exists(backupPath))
                throw new InvalidOperationException("Este ajuste já possui backup. Desative-o antes de aplicar novamente.");
            if (IsApplied(id))
                throw new InvalidOperationException("O ajuste já está ativo no Windows e não foi alterado pelo ynoez.");

            var backup = new SystemRegistryBackup(id,
                definition.Values.Select(Capture).ToList(), DateTimeOffset.UtcNow);
            WriteAtomic(backupPath, JsonSerializer.Serialize(backup, JsonOptions));

            try
            {
                RunElevated(BuildApplyScript(definition));
                if (!IsApplied(id))
                    throw new InvalidOperationException("O Windows não confirmou todos os valores do Registro.");
                AppLog.Write("Ajuste de sistema aplicado: " + id + ". Backup original salvo.");
            }
            catch (OperationCanceledException)
            {
                // UAC cancellation means the elevated script never started.
                try { if (File.Exists(backupPath)) File.Delete(backupPath); } catch { }
                throw;
            }
            catch (Exception applyError)
            {
                try
                {
                    RestoreCore(definition, backup, backupPath);
                }
                catch (Exception restoreError)
                {
                    throw new InvalidOperationException(
                        "A aplicação falhou e a reversão automática ficou incompleta. O backup foi preservado. " +
                        applyError.Message + " | Restauração: " + restoreError.Message, applyError);
                }
                throw;
            }
        }
    }

    public static void Restore(string id)
    {
        lock (Sync)
        {
            var definition = GetDefinition(id);
            var backupPath = GetBackupPath(id);
            if (!File.Exists(backupPath))
                throw new InvalidOperationException("Não existe backup original para este ajuste.");

            var backup = JsonSerializer.Deserialize<SystemRegistryBackup>(
                File.ReadAllText(backupPath), JsonOptions)
                ?? throw new InvalidDataException("Backup do Registro de sistema inválido.");
            if (!string.Equals(backup.Id, id, StringComparison.Ordinal) ||
                backup.Values.Count != definition.Values.Count)
                throw new InvalidDataException("O backup não corresponde exatamente ao ajuste selecionado.");

            // The journal is data, not authority: validate every path/value/type against
            // the closed catalog before creating any elevated PowerShell command.
            var expected = definition.Values;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < backup.Values.Count; index++)
            {
                var snapshot = backup.Values[index];
                var allowed = expected[index];
                if (!string.Equals(snapshot.KeyPath, allowed.KeyPath, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(snapshot.ValueName, allowed.ValueName, StringComparison.OrdinalIgnoreCase) ||
                    (snapshot.Kind is not RegistryValueKind.DWord and not RegistryValueKind.String) ||
                    !seen.Add(snapshot.KeyPath + "|" + snapshot.ValueName) ||
                    (!snapshot.ValueExisted && (snapshot.DwordValue != 0 || snapshot.StringValue is not null)))
                    throw new InvalidDataException("O backup contém um caminho, valor ou tipo fora da allowlist.");
            }

            RestoreCore(definition, backup, backupPath);
            AppLog.Write("Ajuste de sistema restaurado: " + id + ".");
        }
    }

    internal static string BuildApplyScript(string id) => BuildApplyScript(GetDefinition(id));

    private static string BuildApplyScript(Definition definition)
    {
        var commands = new List<string> { "$ErrorActionPreference='Stop'" };
        foreach (var value in definition.Values)
        {
            var path = "HKLM:\\" + Escape(value.KeyPath);
            var name = Escape(value.ValueName);
            commands.Add("if (-not (Test-Path -LiteralPath '" + path + "')) { New-Item -Path '" + path + "' -Force | Out-Null }");
            if (value.Kind == RegistryValueKind.DWord)
                commands.Add("New-ItemProperty -LiteralPath '" + path + "' -Name '" + name +
                    "' -Value ([int]" + value.DwordValue.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    ") -PropertyType DWord -Force | Out-Null");
            else
                commands.Add("New-ItemProperty -LiteralPath '" + path + "' -Name '" + name +
                    "' -Value '" + Escape(value.StringValue ?? "") +
                    "' -PropertyType String -Force | Out-Null");

            commands.Add("$actual=(Get-ItemProperty -LiteralPath '" + path + "' -Name '" + name +
                "' -ErrorAction Stop).'" + name + "'");
            var expected = value.Kind == RegistryValueKind.DWord
                ? value.DwordValue.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : Escape(value.StringValue ?? "");
            commands.Add("if ([string]$actual -ne '" + expected + "') { throw 'Verificação falhou: " + Escape(value.ValueName) + "' }");
        }
        commands.Add("exit 0");
        return string.Join("; ", commands);
    }

    private static string BuildRestoreScript(SystemRegistryBackup backup)
    {
        var commands = new List<string> { "$ErrorActionPreference='Stop'" };
        foreach (var value in backup.Values)
        {
            var path = "HKLM:\\" + Escape(value.KeyPath);
            var name = Escape(value.ValueName);
            if (value.ValueExisted)
            {
                commands.Add("if (-not (Test-Path -LiteralPath '" + path + "')) { New-Item -Path '" + path + "' -Force | Out-Null }");
                if (value.Kind == RegistryValueKind.DWord)
                    commands.Add("New-ItemProperty -LiteralPath '" + path + "' -Name '" + name +
                        "' -Value ([int]" + value.DwordValue.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        ") -PropertyType DWord -Force | Out-Null");
                else
                    commands.Add("New-ItemProperty -LiteralPath '" + path + "' -Name '" + name +
                        "' -Value '" + Escape(value.StringValue ?? "") +
                        "' -PropertyType String -Force | Out-Null");
            }
            else
            {
                commands.Add("if (Test-Path -LiteralPath '" + path + "') { Remove-ItemProperty -LiteralPath '" +
                    path + "' -Name '" + name + "' -ErrorAction SilentlyContinue }");
                if (!value.KeyExisted)
                    commands.Add("if (Test-Path -LiteralPath '" + path + "') { $k=Get-Item -LiteralPath '" + path +
                        "'; if ($k.ValueCount -eq 0 -and $k.SubKeyCount -eq 0) { Remove-Item -LiteralPath '" + path + "' -Force } }");
            }
        }
        commands.Add("exit 0");
        return string.Join("; ", commands);
    }

    private static void RestoreCore(Definition definition, SystemRegistryBackup backup, string backupPath)
    {
        RunElevated(BuildRestoreScript(backup));
        foreach (var value in backup.Values)
        {
            if (!MatchesSnapshot(value))
                throw new InvalidOperationException("A restauração não foi confirmada para " + value.ValueName + "; o backup foi mantido.");
        }
        File.Delete(backupPath);
    }

    private static SystemRegistryValueSnapshot Capture(SystemRegistryValueDefinition definition)
    {
        using var key = Registry.LocalMachine.OpenSubKey(definition.KeyPath, writable: false);
        var keyExisted = key is not null;
        var valueExists = keyExisted &&
            key!.GetValueNames().Contains(definition.ValueName, StringComparer.OrdinalIgnoreCase);
        if (!valueExists)
            return new SystemRegistryValueSnapshot(
                definition.KeyPath, definition.ValueName, keyExisted, false,
                RegistryValueKind.String, 0, null);

        var kind = key!.GetValueKind(definition.ValueName);
        if (kind is not RegistryValueKind.DWord and not RegistryValueKind.String)
            throw new InvalidOperationException("O tipo original de " + definition.ValueName +
                " não pode ser restaurado automaticamente (" + kind + ").");
        var value = key.GetValue(definition.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return new SystemRegistryValueSnapshot(
            definition.KeyPath, definition.ValueName, true, true, kind,
            kind == RegistryValueKind.DWord ? Convert.ToInt32(value) : 0,
            kind == RegistryValueKind.String ? Convert.ToString(value) : null);
    }

    private static bool IsTargetValuePresent(SystemRegistryValueDefinition definition)
    {
        using var key = Registry.LocalMachine.OpenSubKey(definition.KeyPath, writable: false);
        if (key is null || !key.GetValueNames().Contains(definition.ValueName, StringComparer.OrdinalIgnoreCase))
            return false;
        var kind = key.GetValueKind(definition.ValueName);
        if (kind != definition.Kind) return false;
        var current = key.GetValue(definition.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return definition.Kind == RegistryValueKind.DWord
            ? Convert.ToInt32(current) == definition.DwordValue
            : string.Equals(Convert.ToString(current), definition.StringValue, StringComparison.Ordinal);
    }

    private static bool MatchesSnapshot(SystemRegistryValueSnapshot snapshot)
    {
        using var key = Registry.LocalMachine.OpenSubKey(snapshot.KeyPath, writable: false);
        var exists = key is not null &&
            key.GetValueNames().Contains(snapshot.ValueName, StringComparer.OrdinalIgnoreCase);
        if (exists != snapshot.ValueExisted) return false;
        if (!exists) return true;
        if (key!.GetValueKind(snapshot.ValueName) != snapshot.Kind) return false;
        var current = key.GetValue(snapshot.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return snapshot.Kind == RegistryValueKind.DWord
            ? Convert.ToInt32(current) == snapshot.DwordValue
            : string.Equals(Convert.ToString(current), snapshot.StringValue, StringComparison.Ordinal);
    }

    private static Definition GetDefinition(string id) =>
        ById.TryGetValue(id, out var value)
            ? value
            : throw new InvalidOperationException("Este ajuste não pertence à lista permitida de Registro do sistema.");

    private static string GetBackupPath(string id)
    {
        _ = GetDefinition(id);
        Directory.CreateDirectory(BackupDirectory);
        return Path.Combine(BackupDirectory, id + ".json");
    }

    private static void RunElevated(string script)
    {
        var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
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

        try
        {
            process.Start();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException("A elevação foi cancelada; nenhuma alteração foi aplicada.", ex);
        }

        if (!process.WaitForExit(45000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("A alteração do Registro excedeu 45 segundos.");
        }
        if (process.ExitCode != 0)
            throw new InvalidOperationException("O Windows recusou a alteração do Registro (código " + process.ExitCode + ").");
    }

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static void WriteAtomic(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, contents);
        File.Move(temporary, path, overwrite: true);
    }
}
