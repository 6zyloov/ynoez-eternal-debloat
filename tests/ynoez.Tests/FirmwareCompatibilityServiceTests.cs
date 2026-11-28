using Xunit;
using ynoez.Services;

namespace ynoez.Tests;

public sealed class FirmwareCompatibilityServiceTests
{
    [Fact]
    public void FaceitRequiresSecurityAndVirtualizationFirmwareFeatures()
    {
        var required = FirmwareCompatibilityService.GetRequiredSettingIds(["faceit"]);

        Assert.Contains("secureboot", required);
        Assert.Contains("tpm", required);
        Assert.Contains("iommu", required);
        Assert.Contains("virtualization", required);
        Assert.Contains("cpu-boost", required);
    }

    [Fact]
    public void VanguardPreCheckRequiresTheSameFirmwareFeaturesAsItsPublishedChecklist()
    {
        var required = FirmwareCompatibilityService.GetRequiredSettingIds(["vanguard-precheck"]);

        Assert.Contains("secureboot", required);
        Assert.Contains("tpm", required);
        Assert.Contains("iommu", required);
        Assert.Contains("virtualization", required);
    }

    [Theory]
    [InlineData("easyanticheat")]
    [InlineData("battleye")]
    public void AntiCheatsWithoutUniversalFirmwareRequirementDoNotInventOne(string profile)
    {
        var required = FirmwareCompatibilityService.GetRequiredSettingIds([profile]);

        Assert.DoesNotContain("secureboot", required);
        Assert.DoesNotContain("tpm", required);
        Assert.DoesNotContain("iommu", required);
        Assert.DoesNotContain("virtualization", required);
    }

    [Theory]
    [InlineData("Intel Virtualization Technology", "virtualization", "Intel Virtualization Technology")]
    [InlineData("SVM Mode", "virtualization", "SVM Mode")]
    [InlineData("SecureBoot", "secureboot", "SecureBoot")]
    [InlineData("SecurityChip", "tpm", "SecurityChip")]
    [InlineData("AMD IOMMU", "iommu", "AMD IOMMU")]
    [InlineData("Core Performance Boost", "cpu-boost", "Core Performance Boost")]
    public void FindSettingNameMatchesOnlyKnownFirmwareCapabilityPatterns(
        string settingName, string requirement, string expected)
    {
        var found = FirmwareCompatibilityService.FindSettingName([settingName], requirement);

        Assert.Equal(expected, found);
    }

    [Fact]
    public void FindSettingNameDoesNotGuessFromUnrelatedSettings()
    {
        var found = FirmwareCompatibilityService.FindSettingName(
            ["WakeOnLAN", "FanProfile", "BootOrder"], "iommu");

        Assert.Null(found);
    }

    [Theory]
    [InlineData("Enabled,Disabled", "Enabled")]
    [InlineData("Disable|Enable", "Enable")]
    [InlineData("Off;On", "On")]
    [InlineData("Inactive,Active", "Active")]
    [InlineData("Auto,Manual", null)]
    public void SelectEnabledValueUsesOnlyAnExplicitEnabledOption(string values, string? expected)
    {
        var selected = FirmwareCompatibilityService.SelectEnabledValue(
            values.Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries));

        Assert.Equal(expected, selected);
    }

    [Fact]
    public void UnknownAntiCheatProfileIsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            FirmwareCompatibilityService.GetRequiredSettingIds(["made-up-ac"]));
    }
}
