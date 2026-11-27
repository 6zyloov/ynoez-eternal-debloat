using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Principal;
using System.Text;

namespace ynoez.Services;

public enum WindowsRepairAction
{
    CheckComponentStore,
    ScanComponentStore,
    RepairComponentStore,
    VerifySystemFiles,
    RepairSystemFiles
}

public sealed record WindowsRepairActionInfo(
    WindowsRepairAction Action, string Name, string Description, bool IsRepair, int EstimatedMinutes);

public sealed record WindowsRepairResult(
    WindowsRepairAction Action, DateTimeOffset StartedAt, DateTimeOffset FinishedAt,
    int ExitCode, bool Succeeded, string LogPath, string OutputTail);

/// <summary>
/// Explicit, allowlisted Windows image/component integrity checks and repairs.
/// No caller-provided command line or executable path is accepted.
/// </summary>
public static class WindowsRepairService
{
    private sealed record CommandPlan(string Executable, string[] Arguments, TimeSpan Timeout);

    private static readonly IReadOnlyDictionary<WindowsRepairAction, (WindowsRepairActionInfo Info, CommandPlan Plan)> Catalog =
        new Dictionary<WindowsRepairAction, (WindowsRepairActionInfo, CommandPlan)>
        {
            [WindowsRepairAction.CheckComponentStore] = (
                new(WindowsRepairAction.CheckComponentStore, "DISM — verificar integridade", "Consulta rapidamente se o repositório de componentes do Windows já está marcado como corrompido. Somente leitura.", false, 2),
                new("dism.exe", new[] { "/Online", "/Cleanup-Image", "/CheckHealth" }, TimeSpan.FromMinutes(15))),
            [WindowsRepairAction.ScanComponentStore] = (
                new(WindowsRepairAction.ScanComponentStore, "DISM — analisar componentes", "Executa uma análise completa do repositório de componentes sem reparar nem alterar arquivos. Pode levar vários minutos.", false, 45),
                new("dism.exe", new[] { "/Online", "/Cleanup-Image", "/ScanHealth" }, TimeSpan.FromMinutes(60))),
            [WindowsRepairAction.RepairComponentStore] = (
                new(WindowsRepairAction.RepairComponentStore, "DISM — reparar imagem do Windows", "Tenta reparar o repositório de componentes usando as fontes disponíveis no Windows Update. Pode consumir dados e levar bastante tempo; não reinicia o PC automaticamente.", true, 60),
                new("dism.exe", new[] { "/Online", "/Cleanup-Image", "/RestoreHealth" }, TimeSpan.FromMinutes(120))),
            [WindowsRepairAction.VerifySystemFiles] = (
                new(WindowsRepairAction.VerifySystemFiles, "SFC — verificar arquivos protegidos", "Verifica arquivos protegidos do Windows sem tentar corrigir nada. Somente leitura.", false, 20),
                new("sfc.exe", new[] { "/VerifyOnly" }, TimeSpan.FromMinutes(45))),
            [WindowsRepairAction.RepairSystemFiles] = (
                new(WindowsRepairAction.RepairSystemFiles, "SFC — verificar e reparar arquivos", "Verifica os arquivos protegidos do Windows e tenta substituir arquivos corrompidos usando o repositório local. Não reinicia o PC automaticamente.", true, 45),
                new("sfc.exe", new[] { "/Scannow" }, TimeSpan.FromMinutes(90)))
        };

    public static IReadOnlyList<WindowsRepairActionInfo> GetActions() =>
        Array.AsReadOnly(Catalog.Values.Select(x => x.Info).OrderBy(x => x.Action).ToArray());

    public static async Task<WindowsRepairResult> RunAsync(
        WindowsRepairAction action, IProgress<string>? progress = null, CancellationToken token = default)
    {
        if (!Catalog.TryGetValue(action, out var entry))
            throw new ArgumentOutOfRangeException(nameof(action), "Ação de reparo não autorizada.");

        token.ThrowIfCancellationRequested();
        Directory.CreateDirectory(AppPaths.Logs);

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var logPath = Path.Combine(AppPaths.Logs, $"windows-repair-{action}-{stamp}-{suffix}.log");
        var exitPath = logPath + ".exit";
        var started = DateTimeOffset.Now;
        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(
            BuildPowerShellScript(entry.Plan, logPath, exitPath)));

        progress?.Report("Solicitando permissão administrativa para " + entry.Info.Name + "…");
        var isAdministrator = IsAdministrator();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("powershell.exe",
                "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encodedScript)
            {
                UseShellExecute = true,
                Verb = isAdministrator ? string.Empty : "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            }
        };

        try
        {
            if (!process.Start())
                throw new InvalidOperationException("Não foi possível iniciar o processo de manutenção do Windows.");

            progress?.Report(entry.Info.Name + " em execução. O processo pode levar até " +
                entry.Info.EstimatedMinutes + " minutos; o ynoez continuará respondendo.");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(entry.Plan.Timeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                if (token.IsCancellationRequested) throw;
                throw new TimeoutException("A operação excedeu o limite de tempo. O log parcial foi preservado: " + logPath);
            }

            token.ThrowIfCancellationRequested();
            var recordedExit = ReadExitCode(exitPath);
            if (recordedExit != process.ExitCode)
                throw new InvalidDataException("O código de saída registrado pelo processo administrativo não corresponde ao processo principal. O log foi preservado: " + logPath);

            var output = File.Exists(logPath) ? await File.ReadAllTextAsync(logPath, token) : string.Empty;
            var tail = Tail(output, 18);
            var succeeded = recordedExit == 0;
            var finished = DateTimeOffset.Now;

            AppLog.Write("Manutenção do Windows " + action + " terminou com código " +
                recordedExit.ToString(CultureInfo.InvariantCulture) + ". Log: " + logPath);
            progress?.Report(succeeded
                ? "Operação concluída. Confira o log e reinicie o Windows somente se o próprio Windows recomendar."
                : "A operação terminou com erro (código " + recordedExit + "). O log foi preservado.");

            TryDelete(exitPath);
            return new WindowsRepairResult(action, started, finished, recordedExit, succeeded, logPath, tail);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            TryDelete(exitPath);
            throw new OperationCanceledException("A solicitação de UAC foi cancelada. Nenhum reparo foi iniciado.", ex, token);
        }
        catch (OperationCanceledException)
        {
            AppLog.Write("Manutenção do Windows cancelada: " + action + ". Log parcial: " + logPath);
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Write("Manutenção do Windows falhou (" + action + "): " + ex);
            throw;
        }
    }

    private static string BuildPowerShellScript(CommandPlan plan, string logPath, string exitPath)
    {
        var executable = plan.Executable switch
        {
            "dism.exe" => "dism.exe",
            "sfc.exe" => "sfc.exe",
            _ => throw new InvalidOperationException("Executável fora da lista permitida.")
        };

        var args = string.Join(",", plan.Arguments.Select(x => "'" + EscapePowerShellLiteral(x) + "'"));
        var log = EscapePowerShellLiteral(Path.GetFullPath(logPath));
        var exit = EscapePowerShellLiteral(Path.GetFullPath(exitPath));

        return "$ErrorActionPreference='Stop'; " +
            "$exe=Join-Path $env:windir 'System32\\" + executable + "'; " +
            "$ynoezArgs=@(" + args + "); $code=1; " +
            "[IO.File]::WriteAllText('" + log + "', 'ynoez Windows repair log' + [Environment]::NewLine, [Text.UTF8Encoding]::new($true)); " +
            "try { " +
            "if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Ferramenta do Windows não encontrada.' }; " +
            "& $exe @ynoezArgs 2>&1 | Tee-Object -FilePath '" + log + "' -Append | Out-Null; " +
            "$code=$LASTEXITCODE; if ($null -eq $code) { $code=1 }; " +
            "} catch { " +
            "($_ | Out-String) | Add-Content -LiteralPath '" + log + "' -Encoding UTF8; $code=1; " +
            "}; " +
            "[IO.File]::WriteAllText('" + exit + "', ([int]$code).ToString([Globalization.CultureInfo]::InvariantCulture)); " +
            "exit [int]$code";
    }

    internal static string BuildPowerShellScriptForTest(WindowsRepairAction action, string logPath, string exitPath)
    {
        if (!Catalog.TryGetValue(action, out var entry))
            throw new ArgumentOutOfRangeException(nameof(action));
        return BuildPowerShellScript(entry.Plan, logPath, exitPath);
    }

    internal static bool IsSafeActionCatalog() =>
        Catalog.Count == 5 &&
        Catalog.Values.All(x =>
            (x.Plan.Executable == "dism.exe" &&
             x.Plan.Arguments.Length == 3 &&
             x.Plan.Arguments[0] == "/Online" &&
             x.Plan.Arguments[1] == "/Cleanup-Image" &&
             x.Plan.Arguments[2] is "/CheckHealth" or "/ScanHealth" or "/RestoreHealth") ||
            (x.Plan.Executable == "sfc.exe" &&
             x.Plan.Arguments.Length == 1 &&
             x.Plan.Arguments[0] is "/VerifyOnly" or "/Scannow"));

    private static int ReadExitCode(string path)
    {
        if (!File.Exists(path) ||
            !int.TryParse(File.ReadAllText(path).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
            throw new InvalidDataException("O processo administrativo terminou sem gravar um código de saída verificável. O log parcial foi preservado.");
        return code;
    }

    private static string Tail(string text, int maxLines)
    {
        if (string.IsNullOrWhiteSpace(text)) return "O comando não retornou texto de diagnóstico.";
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(Environment.NewLine, lines.TakeLast(maxLines));
    }

    private static string EscapePowerShellLiteral(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);

    private static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
