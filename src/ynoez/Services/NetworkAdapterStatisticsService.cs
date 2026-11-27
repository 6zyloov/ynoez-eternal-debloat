using System.Globalization;
using System.Net.NetworkInformation;

namespace ynoez.Services;

public sealed record NetworkAdapterStatistics(
    string Name, string Description, string Type, string Status, long LinkSpeedBitsPerSecond,
    long BytesReceived, long BytesSent, long PacketsReceived, long PacketsSent,
    long ReceiveErrors, long SendErrors, long ReceiveDiscards, long SendDiscards,
    DateTimeOffset CapturedAt)
{
    public string LinkSpeedDisplay => NetworkAdapterStatisticsService.FormatLinkSpeed(LinkSpeedBitsPerSecond);
    public string BytesReceivedDisplay => NetworkAdapterStatisticsService.FormatBytes(BytesReceived);
    public string BytesSentDisplay => NetworkAdapterStatisticsService.FormatBytes(BytesSent);
    public string ReceiveDiscardsDisplay => ReceiveDiscards.ToString("N0", CultureInfo.CurrentCulture);
    public string SendDiscardsDisplay => SendDiscards.ToString("N0", CultureInfo.CurrentCulture);
    public string ReceiveProblemPercentDisplay => ReceiveProblemPercent.HasValue ? ReceiveProblemPercent.Value.ToString("0.00", CultureInfo.CurrentCulture) + "%" : "—";
    public string SendProblemPercentDisplay => SendProblemPercent.HasValue ? SendProblemPercent.Value.ToString("0.00", CultureInfo.CurrentCulture) + "%" : "—";

    public double? ReceiveProblemPercent =>
        NetworkAdapterStatisticsService.CalculateProblemPercent(PacketsReceived, ReceiveErrors, ReceiveDiscards);
    public double? SendProblemPercent =>
        NetworkAdapterStatisticsService.CalculateProblemPercent(PacketsSent, SendErrors, SendDiscards);
}

public static class NetworkAdapterStatisticsService
{
    public static IReadOnlyList<NetworkAdapterStatistics> Capture()
    {
        var capturedAt = DateTimeOffset.Now;
        var result = new List<NetworkAdapterStatistics>();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;
            try
            {
                var stats = adapter.GetIPStatistics();
                result.Add(new NetworkAdapterStatistics(
                    adapter.Name,
                    adapter.Description,
                    adapter.NetworkInterfaceType.ToString(),
                    adapter.OperationalStatus.ToString(),
                    adapter.Speed,
                    stats.BytesReceived,
                    stats.BytesSent,
                    stats.UnicastPacketsReceived + stats.NonUnicastPacketsReceived,
                    stats.UnicastPacketsSent + stats.NonUnicastPacketsSent,
                    stats.IncomingPacketsWithErrors,
                    stats.OutgoingPacketsWithErrors,
                    stats.IncomingPacketsDiscarded,
                    stats.OutgoingPacketsDiscarded,
                    capturedAt));
            }
            catch (NetworkInformationException ex)
            {
                AppLog.Write("Estatísticas de adaptador indisponíveis (" + adapter.Name + "): " + ex.Message);
            }
            catch (Exception ex)
            {
                AppLog.Write("Falha ao consultar adaptador (" + adapter.Name + "): " + ex.Message);
            }
        }

        return result.OrderByDescending(x => string.Equals(x.Status, "Up", StringComparison.OrdinalIgnoreCase))
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static double? CalculateProblemPercent(long packets, long errors, long discards)
    {
        if (packets < 0 || errors < 0 || discards < 0)
            throw new ArgumentOutOfRangeException(nameof(packets), "Contadores negativos não são válidos.");
        var total = (double)packets + errors + discards;
        return total <= 0 ? null : (errors + (double)discards) * 100d / total;
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "—";
        string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };
        var value = (double)bytes;
        var index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }
        return value.ToString(index == 0 ? "0" : "0.0", CultureInfo.InvariantCulture) + " " + units[index];
    }

    public static string FormatLinkSpeed(long bitsPerSecond) =>
        bitsPerSecond <= 0 ? "—" :
        bitsPerSecond >= 1_000_000_000
            ? (bitsPerSecond / 1_000_000_000d).ToString("0.##", CultureInfo.InvariantCulture) + " Gbps"
            : bitsPerSecond >= 1_000_000
                ? (bitsPerSecond / 1_000_000d).ToString("0.##", CultureInfo.InvariantCulture) + " Mbps"
                : (bitsPerSecond / 1_000d).ToString("0.##", CultureInfo.InvariantCulture) + " Kbps";
}
