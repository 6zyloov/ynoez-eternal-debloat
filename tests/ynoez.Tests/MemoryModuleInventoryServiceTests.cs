using Xunit;
using ynoez.Services;

namespace ynoez.Tests;

public sealed class MemoryModuleInventoryServiceTests
{
    [Theory]
    [InlineData(20, "DDR")]
    [InlineData(21, "DDR2")]
    [InlineData(24, "DDR3")]
    [InlineData(26, "DDR4")]
    [InlineData(30, "LPDDR4")]
    [InlineData(34, "DDR5")]
    [InlineData(35, "LPDDR5")]
    [InlineData(0, "Não informado")]
    [InlineData(99, "SMBIOS 99")]
    public void GetMemoryTypeDisplay_MapsKnownSmbiosValues(ushort value, string expected)
    {
        Assert.Equal(expected, MemoryModuleInventoryService.GetMemoryTypeDisplay(value));
    }

    [Theory]
    [InlineData(8, "DIMM")]
    [InlineData(12, "SO-DIMM")]
    [InlineData(13, "Micro-DIMM")]
    [InlineData(14, "Mini-DIMM")]
    [InlineData(0, "Não informado")]
    [InlineData(99, "SMBIOS 99")]
    public void GetFormFactorDisplay_MapsKnownSmbiosValues(ushort value, string expected)
    {
        Assert.Equal(expected, MemoryModuleInventoryService.GetFormFactorDisplay(value));
    }

    [Fact]
    public void FormatCapacity_UsesBinaryBytesAndHandlesUnavailableData()
    {
        Assert.Equal("8 GB", MemoryModuleInventoryService.FormatCapacity(8UL * 1024 * 1024 * 1024));
        Assert.Equal("Não informado", MemoryModuleInventoryService.FormatCapacity(0));
    }

    [Fact]
    public void ModuleInfo_UsesConfiguredSpeedAndMarksEccOnlyWhenReported()
    {
        var ecc = new MemoryModuleInfo("DIMM_A1", "BANK 0", 16UL * 1024 * 1024 * 1024,
            3600, 3200, 8, 26, 64, 72, "Example", "EX16G3200");
        var unknown = new MemoryModuleInfo("", "", 0, 0, 0, 0, 0, 0, 0, "", "");

        Assert.Equal("DIMM_A1", ecc.SlotDisplay);
        Assert.Equal("16 GB", ecc.CapacityDisplay);
        Assert.Equal("3600 MHz", ecc.SpeedDisplay);
        Assert.Equal("3200 MHz", ecc.ConfiguredSpeedDisplay);
        Assert.Equal("DDR4", ecc.TypeDisplay);
        Assert.Equal("DIMM", ecc.FormFactorDisplay);
        Assert.Equal("Reportado", ecc.EccDisplay);
        Assert.Equal("Slot não informado", unknown.SlotDisplay);
        Assert.Equal("Não informado", unknown.SpeedDisplay);
        Assert.Equal("Indisponível", unknown.EccDisplay);
    }

    [Fact]
    public void Snapshot_DoesNotClaimSingleOrDualChannelFromModuleCount()
    {
        var modules = new[]
        {
            new MemoryModuleInfo("DIMM_A1", "", 8UL * 1024 * 1024 * 1024, 3200, 3200, 8, 26, 64, 64, "", ""),
            new MemoryModuleInfo("DIMM_B1", "", 8UL * 1024 * 1024 * 1024, 3200, 3200, 8, 26, 64, 64, "", "")
        };
        var snapshot = new MemoryModuleInventorySnapshot(DateTimeOffset.UtcNow, 4, modules);

        Assert.Equal(16UL * 1024 * 1024 * 1024, snapshot.TotalCapacityBytes);
        Assert.Equal("16 GB", snapshot.TotalCapacityDisplay);
        Assert.Contains("2 módulo(s)", snapshot.SlotSummary);
        Assert.Contains("4 dispositivo(s)", snapshot.SlotSummary);
        Assert.Contains("não fornece aqui um indicador confiável", snapshot.Notice);
        Assert.DoesNotContain("Dual Channel", snapshot.Notice, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildCsv_ExportsModuleDataAndNeutralizesFormulaInjection()
    {
        var modules = new[]
        {
            new MemoryModuleInfo("DIMM_A1", "", 16UL * 1024 * 1024 * 1024, 3600, 3200, 8, 26, 64, 72,
                "=UNKNOWN()", "+PARTNUMBER")
        };
        var snapshot = new MemoryModuleInventorySnapshot(DateTimeOffset.UtcNow, 2, modules);

        var csv = MemoryModuleInventoryService.BuildCsv(snapshot);

        Assert.Contains("\"DIMM_A1\"", csv);
        Assert.Contains("\"16 GB\"", csv);
        Assert.Contains("\"DDR4\"", csv);
        Assert.Contains("\"'=UNKNOWN()\"", csv);
        Assert.Contains("\"'+PARTNUMBER\"", csv);
    }

    [Fact]
    public void EscapeCsv_QuotesEmbeddedQuotesAndNeutralizesFormulaPrefixes()
    {
        Assert.Equal("\"'=SUM(1,2)\"", MemoryModuleInventoryService.EscapeCsv("=SUM(1,2)"));
        Assert.Equal("\"'@vendor\"", MemoryModuleInventoryService.EscapeCsv("@vendor"));
        Assert.Equal("\"normal \"\"quoted\"\" value\"", MemoryModuleInventoryService.EscapeCsv("normal \"quoted\" value"));
        Assert.Equal("\"\"", MemoryModuleInventoryService.EscapeCsv(null));
    }
}
