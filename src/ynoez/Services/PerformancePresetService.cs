using Microsoft.Win32;

namespace ynoez.Services;

public sealed record PerformancePresetResult(
    int UserRegistryTweaksApplied,
    int SystemTweaksApplied,
    bool HighPerformancePlanApplied,
    IReadOnlyList<string> Failures)
{
    public bool Succeeded => Failures.Count == 0;
    public string Summary =>
        UserRegistryTweaksApplied + " ajustes de Registro do usuário, " +
        SystemTweaksApplied + " ajuste(s) de sistema e plano de energia " +
        (HighPerformancePlanApplied ? "Alto desempenho aplicado." : "inalterado.") +
        (Failures.Count == 0 ? "" : " Falhas: " + string.Join(" | ", Failures));
}

public static class PerformancePresetService
{
    private static readonly HashSet<string> SafeUserTweakIds = new(StringComparer.Ordinal)
    {
        "gaming-disable-windows-capture",
        "gaming-disable-game-dvr-policy-user",
        "gaming-enable-auto-game-mode",
        "gaming-enable-game-mode",
        "interface-performance-visual-effects",
        "interface-disable-taskbar-animations",
        "interface-disable-transparency",
        "ui-menu-show-delay",
        "ui-mouse-hover-time"
    };

    /// <summary>
    /// Applies a conservative desktop gaming preset. It does not apply undocumented
    /// scheduler hacks, global power-throttling changes, CPU overclocking, or HAGS
    /// without a separate compatibility decision.
    /// </summary>
    public static Task<PerformancePresetResult> ApplyAsync() =>
        Task.Run(Apply);

    /// <summary>
    /// Desfaz o perfil usando somente os backups que o ynoez guardou antes de alterar cada valor.
    /// Itens sem backup são ignorados; nada é restaurado "por palpite".
    /// </summary>
    public static Task<string> RestoreAsync() =>
        Task.Run(Restore);

    internal static string Restore()
    {
        var failures = new List<string>();
        var restoredUser = 0;
        var restoredSystem = 0;
        var powerRestored = false;

        try
        {
            var registry = new RegistryTweakService();
            var tweaks = OptimizationCatalogService.LoadRegistryTweaks()
                .Where(item => SafeUserTweakIds.Contains(item.Id))
                .ToArray();
            foreach (var tweak in tweaks)
            {
                try
                {
                    if (!registry.CanRestore(tweak.Id)) continue;
                    registry.Restore(tweak);
                    restoredUser++;
                }
                catch (Exception ex)
                {
                    failures.Add(tweak.Id + ": " + ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            failures.Add("Registro do usuário: " + ex.Message);
        }

        try
        {
            if (SystemRegistryTweakService.GetStatus("mmcss-games").HasBackup)
            {
                SystemRegistryTweakService.Restore("mmcss-games");
                restoredSystem++;
            }
        }
        catch (Exception ex)
        {
            failures.Add("MMCSS Games: " + ex.Message);
        }

        try
        {
            if (PowerProfileService.CanRestoreOriginal())
            {
                PowerProfileService.RestoreOriginal();
                powerRestored = true;
            }
        }
        catch (Exception ex)
        {
            failures.Add("Plano de energia: " + ex.Message);
        }

        var summary = restoredUser == 0 && restoredSystem == 0 && !powerRestored && failures.Count == 0
            ? "Não há otimização registrada para desfazer."
            : restoredUser + " ajuste(s) do usuário e " + restoredSystem + " ajuste(s) de sistema restaurados; plano de energia " +
              (powerRestored ? "original restaurado." : "inalterado.") +
              (failures.Count == 0 ? "" : " Falhas: " + string.Join(" | ", failures));
        AppLog.Write("Desfazer perfil competitivo: " + summary);
        return summary;
    }

    internal static PerformancePresetResult Apply()
    {
        var failures = new List<string>();
        var appliedUser = 0;
        var appliedSystem = 0;
        var powerApplied = false;

        try
        {
            var registry = new RegistryTweakService();
            var tweaks = OptimizationCatalogService.LoadRegistryTweaks()
                .Where(item => SafeUserTweakIds.Contains(item.Id))
                .ToArray();

            foreach (var tweak in tweaks)
            {
                try
                {
                    if (registry.Inspect(tweak) == TweakState.Enabled) continue;
                    registry.Apply(tweak);
                    appliedUser++;
                }
                catch (Exception ex)
                {
                    failures.Add(tweak.Id + ": " + ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            failures.Add("Registro do usuário: " + ex.Message);
        }

        try
        {
            appliedSystem = SystemRegistryTweakService.ApplyMany(["mmcss-games"]).Count;
        }
        catch (Exception ex)
        {
            failures.Add("MMCSS Games: " + ex.Message);
        }

        try
        {
            _ = PowerProfileService.Apply(PowerProfileMode.Performance);
            powerApplied = true;
        }
        catch (Exception ex)
        {
            failures.Add("Plano de energia: " + ex.Message);
        }

        var result = new PerformancePresetResult(appliedUser, appliedSystem, powerApplied, failures);
        AppLog.Write("Perfil competitivo do ynoez: " + result.Summary);
        return result;
    }
}
