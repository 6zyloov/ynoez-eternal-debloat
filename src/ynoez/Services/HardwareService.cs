using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace ynoez.Services;

public sealed class HardwareSnapshot
{
    public string Cpu { get; init; } = "Não detectado";
    public string Gpu { get; init; } = "Não detectada";
    public string Memory { get; init; } = "Não detectada";
    public string OperatingSystem { get; init; } = "Não detectado";
    public string WindowsBuild { get; init; } = "—";
    public string BoardManufacturer { get; init; } = "Não detectada";
    public string BoardProduct { get; init; } = "Não detectado";
    public string BoardVersion { get; init; } = "—";
    public string BiosManufacturer { get; init; } = "—";
    public string BiosVersion { get; init; } = "—";
    public string BiosReleaseDate { get; init; } = "—";
    public string ComputerModel { get; init; } = "—";
    public string NetworkAdapters { get; init; } = "Não detectados";
    public string NetworkSummary { get; init; } = "—";
    public bool IsA520MKV2 => (BoardProduct + " " + BoardManufacturer).Contains("A520M K V2", StringComparison.OrdinalIgnoreCase)
        || (BoardProduct + " " + BoardManufacturer).Replace(" ", "").Contains("A520MKV2", StringComparison.OrdinalIgnoreCase);
}

public static class HardwareService
{
    public static Task<HardwareSnapshot> GetSnapshotAsync() => Task.Run(GetSnapshot);

    public static HardwareSnapshot GetSnapshot()
    {
        var cpu = First("SELECT Name FROM Win32_Processor", "Name");
        var gpuNames = Many("SELECT Name FROM Win32_VideoController", "Name");
        var osName = First("SELECT Caption FROM Win32_OperatingSystem", "Caption");
        var osVersion = First("SELECT Version, BuildNumber FROM Win32_OperatingSystem", "Version");
        var build = First("SELECT BuildNumber FROM Win32_OperatingSystem", "BuildNumber");
        var manufacturer = First("SELECT Manufacturer FROM Win32_BaseBoard", "Manufacturer");
        var product = First("SELECT Product FROM Win32_BaseBoard", "Product");
        var boardVersion = First("SELECT Version FROM Win32_BaseBoard", "Version");
        var biosManufacturer = First("SELECT Manufacturer FROM Win32_BIOS", "Manufacturer");
        var biosVersion = First("SELECT SMBIOSBIOSVersion FROM Win32_BIOS", "SMBIOSBIOSVersion");
        var biosDateRaw = First("SELECT ReleaseDate FROM Win32_BIOS", "ReleaseDate");
        var model = First("SELECT Model FROM Win32_ComputerSystem", "Model");
        var totalBytes = SumMemory();
        var adapters = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .Select(n => n.Name + " (" + n.NetworkInterfaceType + ")")
            .ToArray();
        var networkSummary = adapters.Length == 0 ? "Nenhum adaptador ativo" : string.Join(" • ", adapters.Take(2));

        return new HardwareSnapshot
        {
            Cpu = EmptyAs(cpu, "Não detectado"),
            Gpu = gpuNames.Count == 0 ? "Não detectada" : string.Join("\n", gpuNames.Distinct()),
            Memory = totalBytes > 0 ? $"{Math.Round(totalBytes / 1024d / 1024d / 1024d, 1):0.#} GB" : "Não detectada",
            OperatingSystem = EmptyAs(osName, RuntimeInformation.OSDescription),
            WindowsBuild = string.IsNullOrWhiteSpace(build) ? EmptyAs(osVersion, "—") : $"{build} ({osVersion})",
            BoardManufacturer = EmptyAs(manufacturer, "Não detectada"),
            BoardProduct = EmptyAs(product, "Não detectado"),
            BoardVersion = EmptyAs(boardVersion, "—"),
            BiosManufacturer = EmptyAs(biosManufacturer, "—"),
            BiosVersion = EmptyAs(biosVersion, "—"),
            BiosReleaseDate = FormatWmiDate(biosDateRaw),
            ComputerModel = EmptyAs(model, "—"),
            NetworkAdapters = adapters.Length == 0 ? "Não detectados" : string.Join("\n", adapters),
            NetworkSummary = networkSummary
        };
    }

    private static string First(string query, string property)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            foreach (ManagementObject item in searcher.Get())
            {
                using (item)
                {
                    var value = item[property]?.ToString();
                    if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
                }
            }
        }
        catch (Exception ex) { AppLog.Write($"WMI ({property}): {ex.Message}"); }
        return string.Empty;
    }

    private static List<string> Many(string query, string property)
    {
        var values = new List<string>();
        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            foreach (ManagementObject item in searcher.Get())
            {
                using (item)
                {
                    var value = item[property]?.ToString();
                    if (!string.IsNullOrWhiteSpace(value)) values.Add(value.Trim());
                }
            }
        }
        catch (Exception ex) { AppLog.Write($"WMI ({property}): {ex.Message}"); }
        return values;
    }

    private static ulong SumMemory()
    {
        ulong total = 0;
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Capacity FROM Win32_PhysicalMemory");
            foreach (ManagementObject item in searcher.Get())
            {
                using (item)
                {
                    if (ulong.TryParse(item["Capacity"]?.ToString(), out var capacity)) total += capacity;
                }
            }
        }
        catch (Exception ex) { AppLog.Write($"WMI (RAM): {ex.Message}"); }
        return total;
    }

    private static string EmptyAs(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string FormatWmiDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "—";
        try { return System.Management.ManagementDateTimeConverter.ToDateTime(value).ToString("dd/MM/yyyy"); }
        catch { return value; }
    }
}
