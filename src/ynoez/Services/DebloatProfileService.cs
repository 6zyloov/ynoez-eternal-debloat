namespace ynoez.Services;

public sealed record DebloatProfileResult(
    int AppxRemoved,
    int ServicesDisabled,
    int TasksDisabled,
    int FilesDeleted,
    long BytesFreed,
    IReadOnlyList<string> Failures,
    IReadOnlyList<string> Report)
{
    /// <summary>Relatório linha a linha: removido, desativado, ignorado, mantido e falhas.</summary>
    public string ReportText => string.Join("\n", Report);

    public string Summary =>
        AppxRemoved + " apps removidos • " + ServicesDisabled + " serviços desativados • " +
        TasksDisabled + " tarefas desativadas • " + FilesDeleted + " arquivos limpos (" +
        FormatBytes(BytesFreed) + ")" +
        (Failures.Count == 0 ? "" : " • " + Failures.Count + " operação(ões) não concluída(s)");
    internal static string FormatBytes(long bytes) =>
        bytes < 1024 * 1024 ? (bytes / 1024d).ToString("0") + " KB" :
        bytes < 1024L * 1024 * 1024 ? (bytes / 1024d / 1024d).ToString("0.0") + " MB" :
        (bytes / 1024d / 1024d / 1024d).ToString("0.00") + " GB";
}

public static class DebloatProfileService
{
    // Only optional built-in apps from AppxDebloatService's closed allowlist are removed.
    // Xbox, Store, runtimes, drivers, anti-cheats, Defender, Windows Update and Search stay.
    private static readonly string[] OptionalServices =
    [
        "DiagTrack",
        "dmwappushservice",
        "MapsBroker",
        "Fax",
        "RetailDemo",
        "WMPNetworkSvc"
    ];

    public static Task<DebloatProfileResult> RunAsync(Action<string>? progress = null) =>
        Task.Run(() => Run(progress));

    private static DebloatProfileResult Run(Action<string>? progress)
    {
        var failures = new List<string>();
        var report = new List<string>();
        var removedApps = 0;
        var disabledServices = 0;
        var disabledTasks = 0;
        var filesDeleted = 0;
        long bytesFreed = 0;

        progress?.Invoke("Removendo aplicativos Windows opcionais…");
        try
        {
            var packages = AppPackageInventoryService.GetCurrentUserPackagesAsync(CancellationToken.None)
                .GetAwaiter().GetResult();
            var candidates = AppxDebloatService.FindCandidates(packages);
            foreach (var candidate in candidates)
            {
                try
                {
                    AppxDebloatService.RemoveCurrentUserPackageAsync(candidate)
                        .GetAwaiter().GetResult();
                    removedApps++;
                    report.Add("REMOVIDO    " + candidate.DisplayName +
                        " — sem restauração automática; reinstale pela Microsoft Store se quiser de volta.");
                }
                catch (Exception ex)
                {
                    failures.Add(candidate.Name + ": " + ex.Message);
                    report.Add("FALHOU      " + candidate.DisplayName + " — " + ex.Message);
                }
            }

            var skippedApps = AppxDebloatService.GetAllowlistedPackageNames()
                .Count(name => !candidates.Any(candidate =>
                    string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)));
            if (skippedApps > 0)
                report.Add("IGNORADO    " + skippedApps +
                    " aplicativo(s) da lista permitida: não instalado(s), já removido(s) ou não removível(is).");
        }
        catch (Exception ex) { failures.Add("AppX: " + ex.Message); }

        progress?.Invoke("Desativando serviços opcionais seguros…");
        try
        {
            var changedServices = new ServiceTweakService().DisableMany(OptionalServices);
            disabledServices = changedServices.Count;
            foreach (var name in changedServices)
                report.Add("DESATIVADO  serviço " + name + " — restaurável pelo backup de serviços do ynoez.");
            var untouchedServices = OptionalServices
                .Where(name => !changedServices.Contains(name, StringComparer.OrdinalIgnoreCase))
                .ToArray();
            if (untouchedServices.Length > 0)
                report.Add("IGNORADO    serviços já desativados ou ausentes: " +
                    string.Join(", ", untouchedServices) + ".");
        }
        catch (Exception ex) { failures.Add("Serviços: " + ex.Message); }

        progress?.Invoke("Desativando tarefas de telemetria e mapas…");
        try
        {
            var changedTasks = new ScheduledTaskTweakService().DisableMany();
            disabledTasks = changedTasks.Count;
            report.Add(disabledTasks > 0
                ? "DESATIVADO  " + disabledTasks + " tarefa(s) agendada(s) de telemetria/mapas — restaurável pelo backup de tarefas."
                : "IGNORADO    tarefas agendadas: nenhuma pendente de desativação.");
        }
        catch (Exception ex) { failures.Add("Tarefas: " + ex.Message); }

        progress?.Invoke("Limpando temporários e caches…");
        try
        {
            var results = Task.WhenAll(
                CleanupService.ClearWindowsTempAsync(),
                CleanupService.ClearBraveCacheAsync(),
                CleanupService.ClearDiscordCacheAsync()).GetAwaiter().GetResult();
            filesDeleted = results.Sum(x => x.DeletedFiles);
            bytesFreed = results.Sum(x => x.FreedBytes);
            report.Add("LIMPO       " + filesDeleted + " arquivo(s), " + DebloatProfileResult.FormatBytes(bytesFreed) +
                " em temporários e caches do Brave/Discord. Perfis, senhas e cookies foram preservados.");
        }
        catch (Exception ex) { failures.Add("Limpeza: " + ex.Message); }

        report.Add("MANTIDO     Microsoft Store, Xbox, Calculadora, Bloco de Notas, Área de Transferência, Pesquisa, " +
            "NVIDIA Instant Replay (Alt+Z), Defender, Firewall, Windows Update, rede, áudio, impressão, drivers e anti-cheats.");
        var result = new DebloatProfileResult(
            removedApps, disabledServices, disabledTasks, filesDeleted, bytesFreed, failures, report);
        AppLog.Write("Debloat automático: " + result.Summary);
        foreach (var failure in failures) AppLog.Write("Debloat: " + failure);
        return result;
    }
}
