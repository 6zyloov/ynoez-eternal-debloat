using Xunit;
using ynoez.Services;

namespace ynoez.Tests;

public sealed class NetworkAdapterAdvancedDiagnosticsServiceTests
{
    private const string Sample = """
    {
      "CapturedAt": "2026-11-27T21:30:00+00:00",
      "Properties": [
        {
          "AdapterName": "Ethernet",
          "AdapterDescription": "Realtek PCIe 2.5GbE Family Controller",
          "DisplayName": "Interrupt Moderation",
          "DisplayValue": "Enabled",
          "RegistryKeyword": "*InterruptModeration",
          "RegistryValue": "1"
        },
        {
          "AdapterName": "Ethernet",
          "AdapterDescription": "Realtek PCIe 2.5GbE Family Controller",
          "DisplayName": "Receive Side Scaling",
          "DisplayValue": "Disabled",
          "RegistryKeyword": "*RSS",
          "RegistryValue": "0"
        },
        {
          "AdapterName": "Ethernet",
          "AdapterDescription": "Realtek PCIe 2.5GbE Family Controller",
          "DisplayName": "Large Send Offload v2 (IPv4)",
          "DisplayValue": "Enabled",
          "RegistryKeyword": "*LSO_TCPv4",
          "RegistryValue": "1"
        },
        {
          "AdapterName": "Ethernet",
          "AdapterDescription": "Realtek PCIe 2.5GbE Family Controller",
          "DisplayName": "Energy Efficient Ethernet",
          "DisplayValue": "Enabled",
          "RegistryKeyword": "*EEE",
          "RegistryValue": "1"
        },
        {
          "AdapterName": "Ethernet",
          "AdapterDescription": "Realtek PCIe 2.5GbE Family Controller",
          "DisplayName": "Speed & Duplex",
          "DisplayValue": "1.0 Gbps Full Duplex",
          "RegistryKeyword": "*SpeedDuplex",
          "RegistryValue": "1000"
        },
        {
          "AdapterName": "Ethernet",
          "AdapterDescription": "Realtek PCIe 2.5GbE Family Controller",
          "DisplayName": "Vendor Custom Property",
          "DisplayValue": "Vendor default",
          "RegistryKeyword": "*VendorCustom",
          "RegistryValue": "Default"
        },
        {
          "AdapterName": "Ethernet",
          "AdapterDescription": "Realtek PCIe 2.5GbE Family Controller",
          "DisplayName": "",
          "DisplayValue": "Custom",
          "RegistryKeyword": "*UndocumentedProperty",
          "RegistryValue": "42"
        }
      ],
      "Errors": []
    }
    """;

    [Fact]
    public void ParseJson_ClassifiesKnownPropertiesWithoutApplyingChanges()
    {
        var snapshot = NetworkAdapterAdvancedDiagnosticsService.ParseJson(Sample);

        Assert.Equal(7, snapshot.Properties.Count);
        Assert.Contains(snapshot.Properties, x =>
            x.DisplayName == "Interrupt Moderation" &&
            x.Category == "Latência" &&
            x.Assessment.Contains("Trade-off", StringComparison.Ordinal));
        Assert.Contains(snapshot.Properties, x =>
            x.DisplayName == "Receive Side Scaling" &&
            x.Assessment.Contains("Desativado", StringComparison.Ordinal));
        Assert.Contains(snapshot.Properties, x =>
            x.DisplayName.StartsWith("Large Send Offload", StringComparison.Ordinal) &&
            x.Category == "Offload");
        Assert.Contains(snapshot.Properties, x =>
            x.DisplayName == "Speed & Duplex" &&
            x.Assessment == "Velocidade/duplex fixos");
        Assert.Contains(snapshot.Properties, x =>
            x.DisplayName == "Vendor Custom Property" &&
            x.Category == "Outros");
        Assert.Contains(snapshot.Properties, x =>
            x.DisplayName == "Energy Efficient Ethernet" &&
            x.Category == "Energia");
        Assert.Contains(snapshot.Properties, x =>
            x.DisplayName == "*UndocumentedProperty (sem nome visível)" &&
            x.RegistryValue == "42" &&
            x.Category == "Outros");
    }

    [Fact]
    public void ParseJson_RejectsMissingCaptureTime()
    {
        const string json = """{"Properties":[],"Errors":[]}""";

        Assert.Throws<InvalidDataException>(() =>
            NetworkAdapterAdvancedDiagnosticsService.ParseJson(json));
    }

    [Fact]
    public void ParseJson_PreservesWarningsAndAcceptsEmptyPropertyList()
    {
        const string json = """
        {
          "CapturedAt": "2026-11-27T21:30:00+00:00",
          "Properties": [],
          "Errors": ["Get-NetAdapterAdvancedProperty indisponível"]
        }
        """;

        var snapshot = NetworkAdapterAdvancedDiagnosticsService.ParseJson(json);

        Assert.Empty(snapshot.Properties);
        Assert.Single(snapshot.Errors);
        Assert.Contains("indisponível", snapshot.Errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Script_IsReadOnlyAndEscapesOutputPath()
    {
        var script = NetworkAdapterAdvancedDiagnosticsService.BuildScriptForTest(@"C:\ynoez\test'file.json");

        Assert.Contains("Get-NetAdapter -Physical", script);
        Assert.Contains("Get-NetAdapterAdvancedProperty", script);
        Assert.Contains("test''file.json", script);
        Assert.DoesNotContain("Set-NetAdapterAdvancedProperty", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Set-NetAdapter", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Remove-NetAdapter", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("$errors = New-Object System.Collections.Generic.List[string]", script);
        Assert.Contains("$errors.Add(", script);
        Assert.Contains("Errors = @($errors.ToArray())", script);
        Assert.DoesNotContain("$warnings", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("=HYPERLINK(\"https://example.invalid\")", "\"'=HYPERLINK(\"\"https://example.invalid\"\")\"")]
    [InlineData("Normal text", "\"Normal text\"")]
    public void EscapeCsv_PreventsFormulaInjection(string input, string expected)
    {
        Assert.Equal(expected, NetworkAdapterAdvancedDiagnosticsService.EscapeCsv(input));
    }
}
