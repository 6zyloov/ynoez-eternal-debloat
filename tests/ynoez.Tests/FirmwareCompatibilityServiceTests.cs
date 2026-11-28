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
    }

    [Theory]
    [InlineData("faceit")]
    [InlineData("riot-vanguard")]
    [InlineData("vanguard-precheck")]
    [InlineData("easyanticheat")]
    [InlineData("battleye")]
    public void NoAntiCheatProfileImpliesCpuBoost(string profile)
    {
        var required = FirmwareCompatibilityService.GetRequiredSettingIds([profile]);
        Assert.DoesNotContain("cpu-boost", required);
    }

    [Theory]
    [InlineData("LENOVO", "lenovo", true)]
    [InlineData("ASUSTeK COMPUTER INC.", "asus", true)]
    [InlineData("Gigabyte Technology Co., Ltd.", "gigabyte", true)]
    [InlineData("Micro-Star International Co., Ltd.", "asus", false)]
    [InlineData("", "lenovo", false)]
    [InlineData(null, "lenovo", false)]
    public void ManufacturerMatchingIgnoresCaseAndPunctuation(string? manufacturer, string token, bool expected) =>
        Assert.Equal(expected, FirmwareCompatibilityService.ManufacturerMatches(manufacturer, token));

    [Theory]
    [InlineData("Gigabyte Technology Co., Ltd.", "A520M K V2", true)]
    [InlineData("GIGABYTE", "A520M K V2-CF", true)]
    [InlineData("Gigabyte Technology Co., Ltd.", "A520M S2H", false)]
    [InlineData("ASUSTeK COMPUTER INC.", "A520M K V2", false)]
    [InlineData(null, null, false)]
    public void A520MKV2ProfileRequiresBothVendorAndProduct(string? manufacturer, string? product, bool expected) =>
        Assert.Equal(expected, FirmwareCompatibilityService.IsGigabyteA520MKV2(manufacturer, product));

    [Fact]
    public void NoSelectedProfileMeansNoRequiredFirmwareSetting()
    {
        Assert.Empty(FirmwareCompatibilityService.GetRequiredSettingIds([]));
        Assert.Empty(FirmwareCompatibilityService.GetTargetSettingIds([], includeCpuBoost: false));
    }

    [Fact]
    public void CpuBoostIsAddedOnlyWhenExplicitlyRequestedAndWithoutDuplicates()
    {
        var withoutBoost = FirmwareCompatibilityService.GetTargetSettingIds(["faceit"], includeCpuBoost: false);
        var withBoost = FirmwareCompatibilityService.GetTargetSettingIds(["faceit", "faceit"], includeCpuBoost: true);
        Assert.DoesNotContain("cpu-boost", withoutBoost);
        Assert.Single(withBoost.Where(x => x == "cpu-boost"));
        Assert.Contains("secureboot", withBoost);
    }

    [Fact]
    public void BoostAloneDoesNotInventAntiCheatRequirements() =>
        Assert.Equal(new[] { "cpu-boost" }, FirmwareCompatibilityService.GetTargetSettingIds([], includeCpuBoost: true));

    [Fact]
    public void ManualGuidanceMentionsCpuBoostOnlyWhenItWasRequested()
    {
        var snapshot = new FirmwareCompatibilitySnapshot(
            "Gigabyte Technology Co., Ltd.", "A520M K V2", "AMD Ryzen 5 5600", "UEFI", "F18",
            true, true, true, "2.0", true, false, false, false,
            false, false, null, false, false, [], []);
        var withoutBoost = FirmwareCompatibilityService.BuildManualGuidance(snapshot, ["faceit"]);
        var withBoost = FirmwareCompatibilityService.BuildManualGuidance(
            snapshot, ["faceit"], FirmwareCompatibilityService.GetTargetSettingIds(["faceit"], true));
        Assert.DoesNotContain("Core Performance Boost", withoutBoost);
        Assert.Contains("Core Performance Boost", withBoost);
    }

    [Fact]
    public void VanguardPreCheckRequiresSecurityAndVirtualizationFirmwareFeatures()
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
    public void FindSettingNameMatchesKnownFirmwareCapabilities(string settingName, string requirement, string expected) =>
        Assert.Equal(expected, FirmwareCompatibilityService.FindSettingName([settingName], requirement));

    [Fact]
    public void FindSettingNameDoesNotGuessFromUnrelatedSettings() =>
        Assert.Null(FirmwareCompatibilityService.FindSettingName(["WakeOnLAN", "FanProfile", "BootOrder"], "iommu"));

    [Theory]
    [InlineData("Enabled,Disabled", "Enabled")]
    [InlineData("Disable|Enable", "Enable")]
    [InlineData("Off;On", "On")]
    [InlineData("Inactive,Active", "Active")]
    [InlineData("Auto,Manual", null)]
    public void SelectEnabledValueUsesOnlyExplicitEnabledOption(string values, string? expected) =>
        Assert.Equal(expected, FirmwareCompatibilityService.SelectEnabledValue(
            values.Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries)));

    [Fact]
    public void DescribeSettingsUsesReadableNamesAndKeepsUnknownIds()
    {
        var text = FirmwareCompatibilityService.DescribeSettings(["secureboot", "cpu-boost", "custom-id"]);
        Assert.Contains("Secure Boot", text);
        Assert.Contains("Core Performance Boost", text);
        Assert.Contains("custom-id", text);
    }

    [Fact]
    public void UnknownAntiCheatProfileIsRejected() =>
        Assert.Throws<ArgumentException>(() => FirmwareCompatibilityService.GetRequiredSettingIds(["made-up-ac"]));
}
