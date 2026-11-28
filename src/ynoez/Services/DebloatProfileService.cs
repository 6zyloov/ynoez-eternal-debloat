namespace ynoez.Services;

public sealed record DebloatProfileResult(
    int AppxRemoved,
    int ServicesDisabled,
    int TasksDisabled,
    int FilesDeleted,
    long BytesFreed,
    IReadOnlyList<string> Failures)
{
    public string Summary =>
        AppxRemoved + " apps removidos • " + ServicesDisabled + " serviços desativados • " +
        TasksDisabled + " tarefas desativadas • " + FilesDeleted + " arquivos limpos (" +
        FormatBytes(BytesFreed) + ")" +
        (Failures.Count == 0 ? "" : " • " + Failures.Count + " operação(ões) não concluída(s)");
    private static string FormatBytes(long bytes) =>
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
                }
                catch (Exception ex)
                {
                    failures.Add(candidate.Name + ": " + ex.Message);
                }
            }
        }
        catch (Exception ex) { failures.Add("AppX: " + ex.Message); }

        progress?.Invoke("Desativando serviços opcionais seguros…");
        try
        {
            disabledServices = new ServiceTweakService().DisableMany(OptionalServices).Count;
        }
        catch (Exception ex) { failures.Add("Serviços: " + ex.Message); }

        progress?.Invoke("Desativando tarefas de telemetria e mapas…");
        try
        {
            disabledTasks = new ScheduledTaskTweakService().DisableMany().Count;
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
        }
        catch (Exception ex) { failures.Add("Limpeza: " + ex.Message); }

        var result = new DebloatProfileResult(
            removedApps, disabledServices, disabledTasks, filesDeleted, bytesFreed, failures);
        AppLog.Write("Debloat automático: " + result.Summary);
        foreach (var failure in failures) AppLog.Write("Debloat: " + failure);
        return result;
    }
}
