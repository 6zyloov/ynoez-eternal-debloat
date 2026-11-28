using Xunit;
using ynoez.Services;

namespace ynoez.Tests;

public sealed class MotherboardDeviceInventoryServiceTests
{
    [Theory]
    [InlineData("Net", "Rede")]
    [InlineData("net", "Rede")]
    [InlineData("MEDIA", "Áudio")]
    [InlineData("media", "Áudio")]
    [InlineData("System", "Sistema / chipset")]
    [InlineData("system", "Sistema / chipset")]
    [InlineData("Unknown", "Outro")]
    [InlineData(null, "Outro")]
    public void GetDeviceCategory_MapsPnPClassesWithoutAssumingVendor(string? pnpClass, string expected)
    {
        Assert.Equal(expected, MotherboardDeviceInventoryService.GetDeviceCategory(pnpClass));
    }

    [Fact]
    public void FormatDriverDate_ParsesDmtfDateAndPreservesUnknownValue()
    {
        Assert.Equal("27/11/2026", MotherboardDeviceInventoryService.FormatDriverDate("20261127000000.000000-000"));
        Assert.Equal("Não reportada", MotherboardDeviceInventoryService.FormatDriverDate(null));
        Assert.Equal("not-a-date", MotherboardDeviceInventoryService.FormatDriverDate("not-a-date"));
    }

    [Fact]
    public void DeviceInfo_ShowsDriverAndConfigurationStateWithoutInventingMissingValues()
    {
        var healthy = new MotherboardDeviceInfo(
            @"PCI\VEN_10EC&DEV_8125", "Realtek PCIe 2.5GbE", "Realtek",
            "Net", "OK", 0, "rt25cx21", "Realtek", "10.73.815.2026",
            "20261127000000.000000-000", "oem42.inf",
            new[] { @"PCI\VEN_10EC&DEV_8125", @"PCI\VEN_10EC" });
        var missingDriver = new MotherboardDeviceInfo(
            @"HDAUDIO\FUNC_01&VEN_10EC", "Realtek Audio", "Realtek",
            "MEDIA", "Error", 28, "", "", "", "", "",
            Array.Empty<string>());

        Assert.Equal("Rede", healthy.CategoryDisplay);
        Assert.Equal("OK", healthy.StateDisplay);
        Assert.Equal("10.73.815.2026", healthy.DriverVersionDisplay);
        Assert.Equal("Realtek", healthy.DriverProviderDisplay);
        Assert.Equal("27/11/2026", healthy.DriverDateDisplay);
        Assert.Contains("VEN_10EC", healthy.HardwareIdsDisplay);

        Assert.Equal("Áudio", missingDriver.CategoryDisplay);
        Assert.Equal("Driver não instalado (código 28)", missingDriver.StateDisplay);
        Assert.Equal("Não reportado", missingDriver.DriverVersionDisplay);
        Assert.Equal("Não reportado", missingDriver.DriverProviderDisplay);
        Assert.Equal("Não reportada", missingDriver.DriverDateDisplay);
        Assert.Equal("Não reportados", missingDriver.HardwareIdsDisplay);
    }

    [Theory]
    [InlineData(10, "Não foi possível iniciar (código 10)")]
    [InlineData(22, "Dispositivo desativado (código 22)")]
    [InlineData(28, "Driver não instalado (código 28)")]
    [InlineData(31, "Windows não conseguiu carregar o driver (código 31)")]
    [InlineData(43, "Windows interrompeu o dispositivo (código 43)")]
    [InlineData(45, "Dispositivo não conectado (código 45)")]
    public void DeviceInfo_MapsCommonConfigManagerErrors(uint code, string expected)
    {
        var device = new MotherboardDeviceInfo(
            "device", "device", "vendor", "Net", "Error", code, "", "", "", "", "", Array.Empty<string>());

        Assert.Equal(expected, device.StateDisplay);
    }

    [Fact]
    public void BuildCsv_ExportsHardwareIdsAndNeutralizesFormulaInjection()
    {
        var device = new MotherboardDeviceInfo(
            @"PCI\VEN_10EC&DEV_8125", "=malicious device", "+FakeVendor",
            "Net", "OK", 0, "service", "Provider", "1.2.3", "20261127000000.000000-000",
            "oem1.inf", new[] { "=HYPERLINK(1)", @"PCI\VEN_10EC" });
        var snapshot = new MotherboardDeviceInventorySnapshot(DateTimeOffset.UtcNow, new[] { device });

        var csv = MotherboardDeviceInventoryService.BuildCsv(snapshot);

        Assert.Contains("\"Rede\"", csv);
        Assert.Contains("\"'=malicious device\"", csv);
        Assert.Contains("\"'+FakeVendor\"", csv);
        Assert.Contains("\"1.2.3\"", csv);
        Assert.Contains("\"oem1.inf\"", csv);
        Assert.Contains("\"'=HYPERLINK(1) | PCI\\VEN_10EC\"", csv);
    }

    [Fact]
    public void EscapeCsv_QuotesEmbeddedQuotesAndNeutralizesFormulaPrefixes()
    {
        Assert.Equal("\"'@vendor\"", MotherboardDeviceInventoryService.EscapeCsv("@vendor"));
        Assert.Equal("\"'-argument\"", MotherboardDeviceInventoryService.EscapeCsv("-argument"));
        Assert.Equal("\"normal \"\"quoted\"\" value\"", MotherboardDeviceInventoryService.EscapeCsv("normal \"quoted\" value"));
        Assert.Equal("\"\"", MotherboardDeviceInventoryService.EscapeCsv(null));
    }
}
