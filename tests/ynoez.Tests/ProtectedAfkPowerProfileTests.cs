using Xunit;
using ynoez.Services;

namespace ynoez.Tests;

public sealed class ProtectedAfkPowerProfileTests
{
    private const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";

    [Fact]
    public void ResolveTarget_ProtectedAfk_UsesBalancedPlanWithoutFallback()
    {
        var resolution = PowerProfileService.ResolveTarget(
            PowerProfileMode.ProtectedAfk,
            alias => alias == "SCHEME_BALANCED" ? BalancedGuid : null);

        Assert.Equal(PowerProfileMode.ProtectedAfk, resolution.RequestedMode);
        Assert.Equal(PowerProfileMode.ProtectedAfk, resolution.AppliedMode);
        Assert.Equal("SCHEME_BALANCED", resolution.RequestedAlias);
        Assert.Equal(BalancedGuid, resolution.Guid);
        Assert.False(resolution.UsedFallback);
    }

    [Fact]
    public void ResolveTarget_ProtectedAfk_RequiresBalancedPlan()
    {
        var queried = new List<string>();

        Assert.Throws<InvalidOperationException>(() =>
            PowerProfileService.ResolveTarget(PowerProfileMode.ProtectedAfk, alias =>
            {
                queried.Add(alias);
                return null;
            }));

        Assert.Equal(new[] { "SCHEME_BALANCED" }, queried);
    }

    [Fact]
    public void ResolveTarget_PerformanceFallbackStillUsesBalancedPlan()
    {
        var resolution = PowerProfileService.ResolveTarget(
            PowerProfileMode.Performance,
            alias => alias == "SCHEME_BALANCED" ? BalancedGuid : null);

        Assert.Equal(PowerProfileMode.Performance, resolution.RequestedMode);
        Assert.Equal(PowerProfileMode.Balanced, resolution.AppliedMode);
        Assert.True(resolution.UsedFallback);
    }
}
