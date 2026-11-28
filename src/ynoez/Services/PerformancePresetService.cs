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
