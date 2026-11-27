using System.Globalization;
using System.Management;
using System.Text;

namespace ynoez.Services;

public sealed record MemoryModuleInfo(
    string DeviceLocator,
    string BankLabel,
    ulong CapacityBytes,
    uint SpeedMhz,
    uint ConfiguredSpeedMhz,
    ushort FormFactor,
    ushort SmbiosMemoryType,
    uint DataWidth,
    uint TotalWidth,
    string Manufacturer,
    string PartNumber)
{
    public string SlotDisplay =>
        !string.IsNullOrWhiteSpace(DeviceLocator) ? DeviceLocator :
        !string.IsNullOrWhiteSpace(BankLabel) ? BankLabel : "Slot não informado";

    public string CapacityDisplay => MemoryModuleInventoryService.FormatCapacity(CapacityBytes);
    public string SpeedDisplay => SpeedMhz > 0 ? SpeedMhz.ToString(CultureInfo.CurrentCulture) + " MHz" : "Não informado";
    public string ConfiguredSpeedDisplay => ConfiguredSpeedMhz > 0
        ? ConfiguredSpeedMhz.ToString(CultureInfo.CurrentCulture) + " MHz"
        : "Não informado";
    public string TypeDisplay => MemoryModuleInventoryService.GetMemoryTypeDisplay(SmbiosMemoryType);
    public string FormFactorDisplay => MemoryModuleInventoryService.GetFormFactorDisplay(FormFactor);
    public string EccDisplay => DataWidth > 0 && TotalWidth > DataWidth ? "Reportado" :
        DataWidth > 0 && TotalWidth > 0 ? "Não reportado" : "Indisponível";
    public string ManufacturerDisplay => string.IsNullOrWhiteSpace(Manufacturer) ? "Não informado" : Manufacturer;
    public string PartNumberDisplay => string.IsNullOrWhiteSpace(PartNumber) ? "Não informado" : PartNumber;
}

public sealed record MemoryModuleInventorySnapshot(
    DateTimeOffset CapturedAt,
    int? FirmwareReportedSlotCount,
    IReadOnlyList<MemoryModuleInfo> Modules)
{
    public ulong TotalCapacityBytes => Modules.Aggregate(0UL, (total, module) =>
        ulong.MaxValue - total < module.CapacityBytes ? ulong.MaxValue : total + module.CapacityBytes);

    public string TotalCapacityDisplay => MemoryModuleInventoryService.FormatCapacity(TotalCapacityBytes);
    public string SlotSummary => FirmwareReportedSlotCount is > 0
        ? Modules.Count + " módulo(s) detectado(s) • firmware declara " + FirmwareReportedSlotCount + " dispositivo(s)"
        : Modules.Count + " módulo(s) detectado(s) • quantidade de slots não informada pelo firmware";
    public string Notice =>
        "Dados fornecidos pelo WMI/SMBIOS do firmware, não uma leitura SPD completa. O Windows não fornece aqui um indicador confiável do modo single/dual/quad channel; o ynoez não infere isso apenas pela quantidade ou pelo nome dos slots.";
}

public static class MemoryModuleInventoryService
{
    private const string ModuleQuery =
        "SELECT DeviceLocator, BankLabel, Capacity, Speed, ConfiguredClockSpeed, FormFactor, SMBIOSMemoryType, DataWidth, TotalWidth, Manufacturer, PartNumber FROM Win32_PhysicalMemory";

    public static async Task<MemoryModuleInventorySnapshot> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => Capture(cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    internal static MemoryModuleInventorySnapshot Capture(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = new ManagementScope(@"\\.\root\CIMV2");
        var options = new System.Management.EnumerationOptions
        {
            ReturnImmediately = false,
            Rewindable = false,
            Timeout = TimeSpan.FromSeconds(20)
        };

        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(ModuleQuery), options);
        using var results = searcher.Get();
        var modules = new List<MemoryModuleInfo>();

        foreach (ManagementObject item in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (item)
            {
                modules.Add(new MemoryModuleInfo(
                    ReadString(item, "DeviceLocator"),
                    ReadString(item, "BankLabel"),
                    ReadUInt64(item, "Capacity"),
                    ReadUInt32(item, "Speed"),
                    ReadUInt32(item, "ConfiguredClockSpeed"),
                    ReadUInt16(item, "FormFactor"),
                    ReadUInt16(item, "SMBIOSMemoryType"),
                    ReadUInt32(item, "DataWidth"),
                    ReadUInt32(item, "TotalWidth"),
                    ReadString(item, "Manufacturer"),
                    ReadString(item, "PartNumber")));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (modules.Count == 0)
            throw new InvalidOperationException("O WMI não retornou módulos de memória. Confira se o firmware disponibiliza os dados SMBIOS.");

        modules.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.SlotDisplay, b.SlotDisplay));
        return new MemoryModuleInventorySnapshot(DateTimeOffset.UtcNow, ReadFirmwareSlotCount(scope, cancellationToken), modules);
    }

    public static string GetMemoryTypeDisplay(ushort type) => type switch
    {
        20 => "DDR",
        21 => "DDR2",
        22 => "DDR2 FB-DIMM",
        24 => "DDR3",
        26 => "DDR4",
        27 => "LPDDR",
        28 => "LPDDR2",
        29 => "LPDDR3",
        30 => "LPDDR4",
        34 => "DDR5",
        35 => "LPDDR5",
        0 => "Não informado",
        _ => "SMBIOS " + type.ToString(CultureInfo.InvariantCulture)
    };

    public static string GetFormFactorDisplay(ushort formFactor) => formFactor switch
    {
        8 => "DIMM",
        12 => "SO-DIMM",
        13 => "Micro-DIMM",
        14 => "Mini-DIMM",
        0 => "Não informado",
        _ => "SMBIOS " + formFactor.ToString(CultureInfo.InvariantCulture)
    };

    public static string FormatCapacity(ulong bytes)
    {
        if (bytes == 0) return "Não informado";
        var gib = bytes / (1024d * 1024d * 1024d);
        return gib.ToString("0.##", CultureInfo.CurrentCulture) + " GB";
    }

    public static string BuildCsv(MemoryModuleInventorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var rows = new List<string>
        {
            string.Join(",", new[]
            {
                EscapeCsv("DeviceLocator"),
                EscapeCsv("BankLabel"),
                EscapeCsv("Capacity"),
                EscapeCsv("SpeedMHz"),
                EscapeCsv("ConfiguredSpeedMHz"),
                EscapeCsv("MemoryType"),
                EscapeCsv("FormFactor"),
                EscapeCsv("DataWidth"),
                EscapeCsv("TotalWidth"),
                EscapeCsv("ECC"),
                EscapeCsv("Manufacturer"),
                EscapeCsv("PartNumber")
            })
        };

        foreach (var module in snapshot.Modules)
        {
            rows.Add(string.Join(",", new[]
            {
                EscapeCsv(module.DeviceLocator),
                EscapeCsv(module.BankLabel),
                EscapeCsv(module.CapacityDisplay),
                EscapeCsv(module.SpeedDisplay),
                EscapeCsv(module.ConfiguredSpeedDisplay),
                EscapeCsv(module.TypeDisplay),
                EscapeCsv(module.FormFactorDisplay),
                EscapeCsv(module.DataWidth.ToString(CultureInfo.InvariantCulture)),
                EscapeCsv(module.TotalWidth.ToString(CultureInfo.InvariantCulture)),
                EscapeCsv(module.EccDisplay),
                EscapeCsv(module.Manufacturer),
                EscapeCsv(module.PartNumber)
            }));
        }

        return string.Join(Environment.NewLine, rows) + Environment.NewLine;
    }

    public static string EscapeCsv(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static int? ReadFirmwareSlotCount(ManagementScope scope, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                scope,
                new ObjectQuery("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray"),
                new System.Management.EnumerationOptions
                {
                    ReturnImmediately = false,
                    Rewindable = false,
                    Timeout = TimeSpan.FromSeconds(10)
                });
            using var results = searcher.Get();
            var total = 0;
            var found = false;
            foreach (ManagementObject item in results)
            {
                token.ThrowIfCancellationRequested();
                using (item)
                {
                    var count = ReadUInt32(item, "MemoryDevices");
                    if (count == 0) continue;
                    total = checked(total + (int)count);
                    found = true;
                }
            }
            return found ? total : null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            AppLog.Write("Quantidade de slots de memória não foi reportada pelo WMI: " + ex.Message);
            return null;
        }
    }

    private static string ReadString(ManagementObject item, string property) =>
        Convert.ToString(item[property], CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;

    private static uint ReadUInt32(ManagementObject item, string property)
    {
        try { return Convert.ToUInt32(item[property], CultureInfo.InvariantCulture); }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) { return 0; }
    }

    private static ulong ReadUInt64(ManagementObject item, string property)
    {
        try { return Convert.ToUInt64(item[property], CultureInfo.InvariantCulture); }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) { return 0; }
    }

    private static ushort ReadUInt16(ManagementObject item, string property)
    {
        try { return Convert.ToUInt16(item[property], CultureInfo.InvariantCulture); }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) { return 0; }
    }
}
