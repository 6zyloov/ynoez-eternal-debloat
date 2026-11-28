using ynoez.Services;
using Xunit;

namespace ynoez.Tests;

public sealed class NetworkAdapterPerformanceTweakServiceTests
{
    [Theory]
    [InlineData("Enabled", "enabled", true)]
    [InlineData("Disabled", "disabled", true)]
    [InlineData("Ativado", "enabled", true)]
    [InlineData("Desabilitado", "disabled", true)]
    [InlineData("On", "enabled", true)]
    [InlineData("Unknown", "disabled", false)]
    public void FindChoiceIndex_MapsLocalizedDriverValues(string value, string requested, bool found)
    {
        var index = NetworkAdapterPerformanceTweakService.FindChoiceIndex([value], requested);
        Assert.Equal(found, index >= 0);
    }

    [Fact]
    public void BuildSetScript_UsesExactAdapterKeywordAndDriverDisplayValue()
    {
        var script = NetworkAdapterPerformanceTweakService.BuildSetScript(
            "Ethernet", "*InterruptModeration", "Disabled");

        Assert.Contains("Get-NetAdapterAdvancedProperty -Name $n", script);
        Assert.Contains("Set-NetAdapterAdvancedProperty -Name $n", script);
        Assert.Contains("-RegistryKeyword $k", script);
        Assert.Contains("-DisplayValue $v", script);
        Assert.Contains("ValidDisplayValues", script);
        Assert.DoesNotContain("reg.exe", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSetScript_RejectsShellInjectionStrings()
    {
        Assert.Throws<ArgumentException>(() =>
            NetworkAdapterPerformanceTweakService.BuildSetScript("Ethernet';exit 9;'", "*RSS", "Enabled"));
    }
}
