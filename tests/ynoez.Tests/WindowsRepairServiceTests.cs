using Xunit;
using ynoez.Services;

namespace ynoez.Tests;

public sealed class WindowsRepairServiceTests
{
    [Fact]
    public void Catalog_ContainsOnlyAllowlistedReadOnlyAndRepairOperations()
    {
        var actions = WindowsRepairService.GetActions();

        Assert.Equal(5, actions.Count);
        Assert.Equal(actions.Count, actions.Select(x => x.Action).Distinct().Count());
        Assert.Equal(2, actions.Count(x => x.IsRepair));
        Assert.Equal(3, actions.Count(x => !x.IsRepair));
        Assert.All(actions, action => Assert.True(action.EstimatedMinutes > 0));
        Assert.True(WindowsRepairService.IsSafeActionCatalog());
    }

    [Fact]
    public void Catalog_NeverIncludesBootSecurityOrAntiCheatChanges()
    {
        var descriptions = string.Join(" ", WindowsRepairService.GetActions().Select(x => x.Description));

        Assert.DoesNotContain("bcdedit", descriptions, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Vanguard", descriptions, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FACEIT", descriptions, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Secure Boot", descriptions, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TPM", descriptions, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_RejectsUnknownActionBeforeLaunchingAnything()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            WindowsRepairService.RunAsync((WindowsRepairAction)999));
    }
}
