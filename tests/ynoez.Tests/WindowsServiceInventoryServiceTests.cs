using Xunit;
using ynoez.Services;

namespace ynoez.Tests;

public sealed class WindowsServiceInventoryServiceTests
{
    [Theory]
    [InlineData("WSearch")]
    [InlineData("Spooler")]
    [InlineData("WinDefend")]
    [InlineData("wuauserv")]
    [InlineData("BITS")]
    [InlineData("vgc")]
    [InlineData("vgk")]
    [InlineData("FaceitService")]
    [InlineData("NVDisplay.ContainerLocalSystem")]
    public void ClassifyService_KnownCriticalServices_AreProtected(string name)
    {
        var classification = WindowsServiceInventoryService.ClassifyService(name);

        Assert.True(classification.IsProtected);
        Assert.False(classification.IsOptional);
        Assert.Equal("Protegido pelo ynoez", classification.Category);
        Assert.StartsWith("Preservado:", classification.Explanation);
    }

    [Theory]
    [InlineData("DiagTrack")]
    [InlineData("MapsBroker")]
    [InlineData("Fax")]
    [InlineData("RetailDemo")]
    [InlineData("WMPNetworkSvc")]
    public void ClassifyService_OptionalWhitelistServices_AreMarkedAsOptional(string name)
    {
        var classification = WindowsServiceInventoryService.ClassifyService(name);

        Assert.False(classification.IsProtected);
        Assert.True(classification.IsOptional);
        Assert.Equal("Opcional avaliado", classification.Category);
        Assert.Contains("whitelist restrita", classification.Explanation);
    }

    [Theory]
    [InlineData("Winmgmt")]
    [InlineData("Themes")]
    [InlineData("SomeThirdPartyService")]
    public void ClassifyService_UnreviewedServices_RemainReadOnly(string name)
    {
        var classification = WindowsServiceInventoryService.ClassifyService(name);

        Assert.False(classification.IsProtected);
        Assert.False(classification.IsOptional);
        Assert.Equal("Somente leitura", classification.Category);
        Assert.Contains("não oferece alterações", classification.Explanation);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void ClassifyService_RejectsMissingServiceName(string? name)
    {
        Assert.Throws<ArgumentException>(() =>
            WindowsServiceInventoryService.ClassifyService(name!));
    }

    [Fact]
    public void BuildCsv_ExportsReadableSnapshotAndEscapesFormulae()
    {
        var info = new WindowsServiceInfo(
            "ExampleService",
            "=HYPERLINK(1)",
            "+UNTRUSTED()",
            "Running",
            "Auto",
            1234,
            WindowsServiceInventoryService.ClassifyService("ExampleService"));

        var csv = WindowsServiceInventoryService.BuildCsv(new[] { info });

        Assert.Contains("\"ExampleService\"", csv);
        Assert.Contains("\"'=HYPERLINK(1)\"", csv);
        Assert.Contains("\"'+UNTRUSTED()\"", csv);
        Assert.Contains("\"Em execução\"", csv);
        Assert.Contains("\"Automático\"", csv);
        Assert.Contains("\"1234\"", csv);
        Assert.Contains("\"Somente leitura\"", csv);
    }

    [Fact]
    public void EscapeCsv_QuotesEmbeddedQuotesAndNeutralizesFormulaPrefixes()
    {
        Assert.Equal("\"'@vendor\"", WindowsServiceInventoryService.EscapeCsv("@vendor"));
        Assert.Equal("\"'-cmd\"", WindowsServiceInventoryService.EscapeCsv("-cmd"));
        Assert.Equal("\"normal \"\"quoted\"\" value\"", WindowsServiceInventoryService.EscapeCsv("normal \"quoted\" value"));
        Assert.Equal("\"\"", WindowsServiceInventoryService.EscapeCsv(null));
    }

    [Fact]
    public void ServiceDisplayProperties_LocalizeKnownWindowsStatesAndStartModes()
    {
        var classification = WindowsServiceInventoryService.ClassifyService("Themes");
        var running = new WindowsServiceInfo("Themes", "Themes", "", "Running", "Auto", 999, classification);
        var stopped = new WindowsServiceInfo("Themes", "Themes", "", "Stopped", "Disabled", 0, classification);
        var unknown = new WindowsServiceInfo("Themes", "Themes", "", "CustomState", "CustomMode", 0, classification);

        Assert.Equal("Em execução", running.StateDisplay);
        Assert.Equal("Automático", running.StartModeDisplay);
        Assert.Equal("Parado", stopped.StateDisplay);
        Assert.Equal("Desativado", stopped.StartModeDisplay);
        Assert.Equal("—", stopped.ProcessIdDisplay);
        Assert.Equal("CustomState", unknown.StateDisplay);
        Assert.Equal("CustomMode", unknown.StartModeDisplay);
    }
}
