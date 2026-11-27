using System.Globalization;
using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using System.Text;
using ynoez.Services;

namespace ynoez.Services;

public sealed record NetworkAdapterInventory(
    string Name, string Description, string Type, string Status, long LinkSpeedBitsPerSecond,
    string MacAddress, string IpAddresses, string Gateways, string DnsServers,
    bool? DhcpEnabled, int? Ipv4Mtu, int? Ipv6Mtu,
    string DriverManufacturer, string DriverVersion, string DriverDate)
{
    public string LinkSpeedDisplay => NetworkAdapterStatisticsService.FormatLinkSpeed(LinkSpeedBitsPerSecond);
    public string MtuDisplay => NetworkAdapterInventoryService.FormatMtu(Ipv4Mtu, Ipv6Mtu);
    public string DhcpDisplay => DhcpEnabled.HasValue ? (DhcpEnabled.Value ? "Sim" : "Não") : "—";
}

public static class NetworkAdapterInventoryService
{
    private sealed record DriverInfo(string Name, string Manufacturer, string Version, string Date);

    public static IReadOnlyList<NetworkAdapterInventory> Capture()
    {
        var drivers = ReadNetworkDrivers();
        var result = new List<NetworkAdapterInventory>();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;

            try
            {
                var ip = adapter.GetIPProperties();
                var ipv4 = SafeIpv4(ip);
                var ipv6 = SafeIpv6(ip);
                var driver = FindDriver(adapter.Description, drivers);
                result.Add(new NetworkAdapterInventory(
                    adapter.Name,
                    adapter.Description,
                    adapter.NetworkInterfaceType.ToString(),
                    adapter.OperationalStatus.ToString(),
                    adapter.Speed,
                    FormatMac(adapter.GetPhysicalAddress().GetAddressBytes()),
                    JoinAddresses(ip.UnicastAddresses.Select(x => x.Address)),
                    JoinAddresses(ip.GatewayAddresses.Select(x => x.Address)),
                    JoinAddresses(ip.DnsAddresses),
                    ipv4?.IsDhcpEnabled,
                    ipv4?.Mtu,
                    ipv6?.Mtu,
                    driver?.Manufacturer ?? "",
                    driver?.Version ?? "",
                    driver?.Date ?? ""));
            }
            catch (Exception ex)
            {
                AppLog.Write("Interface ignorada no inventário técnico (" + adapter.Name + "): " + ex.Message);
            }
        }

        return result
            .OrderByDescending(x => string.Equals(x.Status, "Up", StringComparison.OrdinalIgnoreCase))
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<DriverInfo> ReadNetworkDrivers()
    {
        var result = new List<DriverInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\CIMV2",
                "SELECT DeviceName, Manufacturer, DriverVersion, DriverDate FROM Win32_PnPSignedDriver WHERE DeviceClass='NET'");
            using var rows = searcher.Get();
            foreach (ManagementObject row in rows)
            {
                using (row)
                {
                    var name = Convert.ToString(row["DeviceName"]) ?? "";
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var date = "";
                    var rawDate = Convert.ToString(row["DriverDate"]);
                    if (!string.IsNullOrWhiteSpace(rawDate))
                    {
                        try { date = ManagementDateTimeConverter.ToDateTime(rawDate).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
                        catch { date = rawDate; }
                    }
                    result.Add(new DriverInfo(name, Convert.ToString(row["Manufacturer"]) ?? "",
                        Convert.ToString(row["DriverVersion"]) ?? "", date));
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("Consulta WMI de drivers de rede indisponível: " + ex.Message);
        }
        return result;
    }

    private static DriverInfo? FindDriver(string description, IReadOnlyList<DriverInfo> drivers)
    {
        var exact = drivers.FirstOrDefault(x => string.Equals(x.Name, description, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;
        return drivers.FirstOrDefault(x =>
            x.Name.Contains(description, StringComparison.OrdinalIgnoreCase) ||
            description.Contains(x.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static IPv4InterfaceProperties? SafeIpv4(IPInterfaceProperties properties)
    {
        try { return properties.GetIPv4Properties(); } catch { return null; }
    }

    private static IPv6InterfaceProperties? SafeIpv6(IPInterfaceProperties properties)
    {
        try { return properties.GetIPv6Properties(); } catch { return null; }
    }

    private static string JoinAddresses(IEnumerable<System.Net.IPAddress> addresses)
    {
        var values = addresses
            .Where(x => !System.Net.IPAddress.Any.Equals(x) && !System.Net.IPAddress.IPv6Any.Equals(x))
            .Select(x => x.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return values.Length == 0 ? "—" : string.Join("; ", values);
    }

    private static string FormatMac(byte[] address)
    {
        return address.Length == 0 ? "—" : string.Join("-", address.Select(x => x.ToString("X2", CultureInfo.InvariantCulture)));
    }

    public static string FormatMtu(int? ipv4, int? ipv6) =>
        (ipv4.HasValue && ipv4.Value > 0 ? ipv4.Value.ToString(CultureInfo.InvariantCulture) : "—") +
        " / " +
        (ipv6.HasValue && ipv6.Value > 0 ? ipv6.Value.ToString(CultureInfo.InvariantCulture) : "—");

    internal static string EscapeCsv(string? value)
    {
        value ??= string.Empty;
        var first = value.FirstOrDefault(c => !char.IsWhiteSpace(c));
        if (first is '=' or '+' or '-' or '@' || first == (char)9 || first == (char)13)
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    public static string ExportCsv(IReadOnlyList<NetworkAdapterInventory> adapters)
    {
        Directory.CreateDirectory(AppPaths.Logs);
        var path = Path.Combine(AppPaths.Logs,
            "network-adapters-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) +
            "-" + Guid.NewGuid().ToString("N")[..8] + ".csv");
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine("Name,Description,Type,Status,LinkSpeedBps,MAC,IPAddresses,Gateways,DNS,DHCP,IPv4MTU,IPv6MTU,DriverManufacturer,DriverVersion,DriverDate");
        foreach (var adapter in adapters)
        {
            writer.WriteLine(string.Join(",",
                EscapeCsv(adapter.Name), EscapeCsv(adapter.Description), EscapeCsv(adapter.Type), EscapeCsv(adapter.Status),
                adapter.LinkSpeedBitsPerSecond.ToString(CultureInfo.InvariantCulture), EscapeCsv(adapter.MacAddress),
                EscapeCsv(adapter.IpAddresses), EscapeCsv(adapter.Gateways), EscapeCsv(adapter.DnsServers),
                adapter.DhcpEnabled?.ToString() ?? "", adapter.Ipv4Mtu?.ToString(CultureInfo.InvariantCulture) ?? "",
                adapter.Ipv6Mtu?.ToString(CultureInfo.InvariantCulture) ?? "", EscapeCsv(adapter.DriverManufacturer),
                EscapeCsv(adapter.DriverVersion), EscapeCsv(adapter.DriverDate)));
        }
        AppLog.Write("Inventário técnico de rede exportado: " + path);
        return path;
    }
}
