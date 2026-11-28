using System.Diagnostics;
using System.Text;
using System.IO;

namespace ynoez.Services;

public sealed record AppxRemovalCandidate(
    string Name,
    string DisplayName,
    string Description,
    string PackageFullName,
    string Version);

/// <summary>
/// Removes only explicitly allowlisted optional AppX apps for the current user.
/// Frameworks, resource packages, bundles, non-removable packages and all-user
/// provisioned packages are never targeted.
/// </summary>
public static class AppxDebloatService
{
    private sealed record Definition(string DisplayName, string Description);

    private static readonly IReadOnlyDictionary<string, Definition> Catalog =
        new Dictionary<string, Definition>(StringComparer.OrdinalIgnoreCase)
        {
            ["Microsoft.BingNews"] = new("Notícias", "Aplicativo de notícias da Microsoft."),
            ["Microsoft.BingWeather"] = new("Clima", "Aplicativo de previsão do tempo."),
            ["Microsoft.GetHelp"] = new("Obter Ajuda", "Atalho de suporte que pode ser reinstalado pela Store."),
            ["Microsoft.Getstarted"] = new("Dicas do Windows", "Dicas iniciais e sugestões do Windows."),
            ["Microsoft.MicrosoftSolitaireCollection"] = new("Microsoft Solitaire", "Coleção de jogos de cartas."),
            ["Microsoft.People"] = new("Pessoas", "Aplicativo opcional de contatos."),
            ["Microsoft.WindowsFeedbackHub"] = new("Hub de Comentários", "Envio de comentários e diagnósticos pelo usuário."),
            ["Microsoft.WindowsMaps"] = new("Mapas", "Aplicativo de mapas offline."),
            ["Microsoft.ZuneMusic"] = new("Groove Música", "Aplicativo de música legado."),
            ["Microsoft.ZuneVideo"] = new("Filmes e TV", "Aplicativo de vídeo legado."),
            ["Microsoft.YourPhone"] = new("Vincular ao Celular", "Integração opcional entre celular e Windows."),
            ["Microsoft.MicrosoftOfficeHub"] = new("Microsoft 365 (Hub)", "Hub promocional do Microsoft 365; não remove Word, Excel ou Office instalados."),
            ["Clipchamp.Clipchamp"] = new("Clipchamp", "Editor de vídeo opcional."),
            ["Microsoft.SkypeApp"] = new("Skype", "Aplicativo Skype, se instalado como pacote AppX."),
            ["Microsoft.WindowsSoundRecorder"] = new("Gravador de Som", "Gravador de áudio opcional."),
            ["Microsoft.Microsoft3DViewer"] = new("Visualizador 3D", "Visualizador 3D opcional legado."),
            ["Microsoft.MixedReality.Portal"] = new("Portal de Realidade Mista", "Componente opcional para dispositivos de realidade mista.")
        };

    public static IReadOnlyList<string> GetAllowlistedPackageNames() =>
        Catalog.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();

    public static IReadOnlyList<AppxRemovalCandidate> FindCandidates(
        IEnumerable<AppxPackageInfo> installedPackages)
    {
        ArgumentNullException.ThrowIfNull(installedPackages);

        return installedPackages
            .Where(package =>
                Catalog.TryGetValue(package.Name, out _) &&
                !package.IsFramework &&
                !package.IsResourcePackage &&
                !package.IsBundle &&
                !package.NonRemovable &&
                string.Equals(package.Status, "Ok", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(package.PackageFullName))
            .GroupBy(package => package.PackageFullName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(package =>
            {
                var definition = Catalog[package.Name];
                return new AppxRemovalCandidate(
                    package.Name, definition.DisplayName, definition.Description,
                    package.PackageFullName, package.Version);
            })
            .OrderBy(candidate => candidate.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public static async Task RemoveCurrentUserPackageAsync(
        AppxRemovalCandidate candidate,
        CancellationToken token = default)
    {
        ValidateCandidate(candidate);
        var script = BuildRemovalScript(candidate);
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
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
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
            throw new TimeoutException("A remoção de " + candidate.DisplayName + " excedeu 60 segundos.");
        }

        token.ThrowIfCancellationRequested();
        var output = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                "O Windows não confirmou a remoção de " + candidate.DisplayName +
                " (código " + process.ExitCode + "). " + error.Trim() + " " + output.Trim());

        AppLog.Write("AppX opcional removido apenas do usuário atual: " +
            candidate.Name + " (" + candidate.PackageFullName + ").");
    }

    internal static string BuildRemovalScript(AppxRemovalCandidate candidate)
    {
        ValidateCandidate(candidate);
        var name = Escape(candidate.Name);
        var fullName = Escape(candidate.PackageFullName);
        return "$ErrorActionPreference='Stop'; " +
            "$name='" + name + "'; $full='" + fullName + "'; " +
            "$pkg=Get-AppxPackage -PackageTypeFilter Main | Where-Object { " +
            "$_.Name -eq $name -and $_.PackageFullName -eq $full -and " +
            "-not $_.IsFramework -and -not $_.IsResourcePackage -and " +
            "-not $_.IsBundle -and -not $_.NonRemovable }; " +
            "if ($null -eq $pkg) { Write-Error 'Pacote não encontrado ou não elegível'; exit 2 }; " +
            "$pkg | Remove-AppxPackage -ErrorAction Stop; " +
            "Start-Sleep -Milliseconds 250; " +
            "if (Get-AppxPackage -Name $name | Where-Object { $_.PackageFullName -eq $full }) { exit 3 }; " +
            "exit 0";
    }

    private static void ValidateCandidate(AppxRemovalCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!Catalog.TryGetValue(candidate.Name, out var definition) ||
            !string.Equals(candidate.DisplayName, definition.DisplayName, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(candidate.PackageFullName) ||
            !candidate.PackageFullName.StartsWith(candidate.Name + "_", StringComparison.OrdinalIgnoreCase) ||
            candidate.PackageFullName.Contains('''))
            throw new InvalidOperationException("Este pacote não pertence à lista fechada de aplicativos opcionais.");
    }

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
