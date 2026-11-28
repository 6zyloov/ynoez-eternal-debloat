using ynoez.Services;
using Xunit;

namespace ynoez.Tests;

public sealed class SystemRegistryTweakServiceTests
{
    [Fact]
    public void Catalog_ContainsOnlyThreeExplicitPerformanceTweaks()
    {
        var catalog = SystemRegistryTweakService.GetCatalog();

        Assert.Equal(3, catalog.Count);
        Assert.Contains(catalog, x => x.Id == "mmcss-games");
        Assert.Contains(catalog, x => x.Id == "network-throttling");
        Assert.Contains(catalog, x => x.Id == "gpu-scheduling");
        Assert.DoesNotContain(catalog, x => x.Id.Contains("hpet", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(catalog, x => x.Id.Contains("bcd", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MmcssScript_ChangesOnlyGamesTaskValues()
    {
        var script = SystemRegistryTweakService.BuildApplyScript("mmcss-games");

        Assert.Contains("Multimedia\\SystemProfile\\Tasks\\Games", script);
        Assert.Contains("Priority", script);
        Assert.Contains("Scheduling Category", script);
        Assert.Contains("Medium", script);
        Assert.DoesNotContain("Win32PrioritySeparation", script);
        Assert.DoesNotContain("bcdedit", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HagsScript_OnlySetsTheWindowsGpuSchedulingPreference()
    {
        var script = SystemRegistryTweakService.BuildApplyScript("gpu-scheduling");

        Assert.Contains("GraphicsDrivers", script);
        Assert.Contains("HwSchMode", script);
        Assert.Contains("([int]2)", script);
        Assert.DoesNotContain("TdrDelay", script);
        Assert.DoesNotContain("DriverStore", script);
    }

    [Fact]
    public void UnknownTweakIdsAreRejected()
    {
        Assert.Throws<InvalidOperationException>(
            () => SystemRegistryTweakService.BuildApplyScript("disable-defender"));
        Assert.Throws<InvalidOperationException>(
            () => SystemRegistryTweakService.BuildApplyScript("disable-windows-update"));
    }
}
