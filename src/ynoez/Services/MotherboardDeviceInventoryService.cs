using System.Globalization;
using System.Management;

namespace ynoez.Services;

public sealed record MotherboardDeviceInfo(
    string DeviceId,
    string Name,
    string Manufacturer,
    string DeviceClass,
    string Status,
    uint ConfigManagerErrorCode,
    string ServiceName,
    string DriverProvider,
    string DriverVersion,
    string DriverDate,
    string InfName,
    IReadOnlyList<string> HardwareIds)
{
    public string CategoryDisplay => DeviceClass switch
    {
        "Net" => "Rede",
        "MEDIA" => "Áudio",
        "System" => "Sistema / chipset",
        _ => string.IsNullOrWhiteSpace(DeviceClass) ? "Outro" : DeviceClass
    };

    public string StateDisplay => ConfigManagerErrorCode switch
    {
        0 => string.Equals(Status, "OK", StringComparison.OrdinalIgnoreCase) ? "OK" : "Sem erro reportado",
        10 => "Não foi possível iniciar (código 10)",
        22 => "Dispositivo desativado (código 22)",
        28 => "Driver não instalado (código 28)",
        31 => "Windows não conseguiu carregar o driver (código 31)",
        43 => "Windows interrompeu o dispositivo (código 43)",
        45 => "Dispositivo não conectado (código 45)",
        _ => "Código de configuração " + ConfigManagerErrorCode.ToString(CultureInfo.InvariantCulture)
    };

    public string DriverVersionDisplay => string.IsNullOrWhiteSpace(DriverVersion) ? "Não reportado" : DriverVersion;
    public string DriverProviderDisplay => string.IsNullOrWhiteSpace(DriverProvider) ? "Não reportado" : DriverProvider;
    public string DriverDateDisplay => MotherboardDeviceInventoryService.FormatDriverDate(DriverDate);
    public string HardwareIdsDisplay => HardwareIds.Count == 0 ? "Não reportados" : string.Join(Environment.NewLine, HardwareIds);
}

public sealed record MotherboardDeviceInventorySnapshot(
    DateTimeOffset CapturedAt,
    IReadOnlyList<MotherboardDeviceInfo> Devices)
{
    public int DeviceCount => Devices.Count;
    public int DevicesWithReportedErrors => Devices.Count(x => x.ConfigManagerErrorCode != 0);
}

public static class MotherboardDeviceInventoryService
{
    private const string DeviceQuery =
        "SELECT Name, DeviceID, PNPClass, Manufacturer, Status, ConfigManagerErrorCode, Service, HardwareID " +
        "FROM Win32_PnPEntity WHERE PNPClass='Net' OR PNPClass='MEDIA' OR PNPClass='System'";

    private const string DriverQuery =
        "SELECT DeviceID, DriverProviderName, DriverVersion, DriverDate, InfName FROM Win32_PnPSignedDriver";

    private sealed record DriverInfo(string Provider, string Version, string Date, string InfName);

    public static async Task<MotherboardDeviceInventorySnapshot> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => Capture(cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    internal static MotherboardDeviceInventorySnapshot Capture(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = new ManagementScope(@"\\.\root\CIMV2");
        var drivers = CaptureDrivers(scope, cancellationToken);
        var devices = CaptureDevices(scope, drivers, cancellationToken);

        if (devices.Count == 0)
            throw new InvalidOperationException("O WMI não retornou dispositivos de rede, áudio ou sistema. Confira se o inventário PnP do Windows está disponível.");

        return new MotherboardDeviceInventorySnapshot(DateTimeOffset.UtcNow, devices);
    }

    public static string FormatDriverDate(string? dmtfDate)
    {
        if (string.IsNullOrWhiteSpace(dmtfDate)) return "Não reportada";
        try
        {
            return ManagementDateTimeConverter.ToDateTime(dmtfDate)
                .ToString("dd/MM/yyyy", CultureInfo.CurrentCulture);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException or ManagementException)
        {
            return dmtfDate;
        }
    }

    public static string GetDeviceCategory(string? pnpClass) => pnpClass?.Trim() switch
    {
        "Net" => "Rede",
        "MEDIA" => "Áudio",
        "System" => "Sistema / chipset",
        _ => "Outro"
    };

    public static string EscapeCsv(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    public static string BuildCsv(MotherboardDeviceInventorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var rows = new List<string>
        {
            string.Join(",", new[]
            {
                EscapeCsv("Category"),
                EscapeCsv("Name"),
                EscapeCsv("Manufacturer"),
                EscapeCsv("Status"),
                EscapeCsv("ConfigManagerErrorCode"),
                EscapeCsv("Service"),
                EscapeCsv("DriverProvider"),
                EscapeCsv("DriverVersion"),
                EscapeCsv("DriverDate"),
                EscapeCsv("InfName"),
                EscapeCsv("DeviceId"),
                EscapeCsv("HardwareIds")
            })
        };

        foreach (var device in snapshot.Devices)
        {
            rows.Add(string.Join(",", new[]
            {
                EscapeCsv(device.CategoryDisplay),
                EscapeCsv(device.Name),
                EscapeCsv(device.Manufacturer),
                EscapeCsv(device.StateDisplay),
                EscapeCsv(device.ConfigManagerErrorCode.ToString(CultureInfo.InvariantCulture)),
                EscapeCsv(device.ServiceName),
                EscapeCsv(device.DriverProvider),
                EscapeCsv(device.DriverVersion),
                EscapeCsv(device.DriverDateDisplay),
                EscapeCsv(device.InfName),
                EscapeCsv(device.DeviceId),
                EscapeCsv(string.Join(" | ", device.HardwareIds))
            }));
        }

        return string.Join(Environment.NewLine, rows) + Environment.NewLine;
    }

    private static Dictionary<string, DriverInfo> CaptureDrivers(
        ManagementScope scope, CancellationToken token)
    {
        var result = new Dictionary<string, DriverInfo>(StringComparer.OrdinalIgnoreCase);
        using var searcher = new ManagementObjectSearcher(
            scope, new ObjectQuery(DriverQuery), CreateOptions(TimeSpan.FromSeconds(20)));
        using var objects = searcher.Get();

        foreach (ManagementObject item in objects)
        {
            token.ThrowIfCancellationRequested();
            using (item)
            {
                var id = ReadString(item, "DeviceID");
                if (string.IsNullOrWhiteSpace(id) || result.ContainsKey(id)) continue;
                result[id] = new DriverInfo(
                    ReadString(item, "DriverProviderName"),
                    ReadString(item, "DriverVersion"),
                    ReadString(item, "DriverDate"),
                    ReadString(item, "InfName"));
            }
        }
        return result;
    }

    private static IReadOnlyList<MotherboardDeviceInfo> CaptureDevices(
        ManagementScope scope,
        IReadOnlyDictionary<string, DriverInfo> drivers,
        CancellationToken token)
    {
        using var searcher = new ManagementObjectSearcher(
            scope, new ObjectQuery(DeviceQuery), CreateOptions(TimeSpan.FromSeconds(20)));
        using var objects = searcher.Get();
        var result = new List<MotherboardDeviceInfo>();

        foreach (ManagementObject item in objects)
        {
            token.ThrowIfCancellationRequested();
            using (item)
            {
                var id = ReadString(item, "DeviceID");
                var name = ReadString(item, "Name");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) continue;

                drivers.TryGetValue(id, out var driver);
                result.Add(new MotherboardDeviceInfo(
                    id,
                    name,
                    ReadString(item, "Manufacturer"),
                    ReadString(item, "PNPClass"),
                    ReadString(item, "Status"),
                    ReadUInt32(item, "ConfigManagerErrorCode"),
                    ReadString(item, "Service"),
                    driver?.Provider ?? "",
                    driver?.Version ?? "",
                    driver?.Date ?? "",
                    driver?.InfName ?? "",
                    ReadStringArray(item, "HardwareID")));
            }
        }

        return result
            .OrderBy(x => x.DeviceClass, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static System.Management.EnumerationOptions CreateOptions(TimeSpan timeout) => new()
    {
        ReturnImmediately = false,
        Rewindable = false,
        Timeout = timeout
    };

    private static string ReadString(ManagementObject item, string property) =>
        Convert.ToString(item[property], CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;

    private static uint ReadUInt32(ManagementObject item, string property)
    {
        try { return Convert.ToUInt32(item[property], CultureInfo.InvariantCulture); }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) { return 0; }
    }

    private static IReadOnlyList<string> ReadStringArray(ManagementObject item, string property)
    {
        try
        {
            if (item[property] is not Array values) return Array.Empty<string>();
            return values.Cast<object?>()
                .Select(x => Convert.ToString(x, CultureInfo.InvariantCulture)?.Trim() ?? "")
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is InvalidCastException or ManagementException)
        {
            return Array.Empty<string>();
        }
    }
}
