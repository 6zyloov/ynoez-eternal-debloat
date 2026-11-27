using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace ynoez.Services;

public sealed record NetworkMonitorSample(
    DateTimeOffset Timestamp, string Gateway, string GatewayStatus, long? GatewayMilliseconds,
    string ExternalStatus, long? ExternalMilliseconds, string ExternalTcpStatus, long? ExternalTcpMilliseconds,
    double GatewayLoss60sPercent, double ExternalLoss60sPercent, double ExternalTcpFailure60sPercent,
    double? GatewayJitter60sMilliseconds, double? ExternalJitter60sMilliseconds, double? ExternalTcpJitter60sMilliseconds,
    string SecondaryExternalStatus = "NOT_TESTED", long? SecondaryExternalMilliseconds = null,
    string SecondaryExternalTcpStatus = "NOT_TESTED", long? SecondaryExternalTcpMilliseconds = null,
    string DnsStatus = "NOT_TESTED", double? DnsMilliseconds = null,
    double SecondaryExternalLoss60sPercent = 0, double SecondaryExternalTcpFailure60sPercent = 0,
    double DnsFailure60sPercent = 0,
    int DnsIpv4AddressCount = 0, int DnsIpv6AddressCount = 0,
    string Ipv6Gateway = "", string Ipv6GatewayStatus = "NOT_TESTED", long? Ipv6GatewayMilliseconds = null,
    string Ipv6ExternalStatus = "NOT_TESTED", long? Ipv6ExternalMilliseconds = null,
    string Ipv6ExternalTcpStatus = "NOT_TESTED", long? Ipv6ExternalTcpMilliseconds = null,
    string Ipv6SecondaryExternalStatus = "NOT_TESTED", long? Ipv6SecondaryExternalMilliseconds = null,
    string Ipv6SecondaryExternalTcpStatus = "NOT_TESTED", long? Ipv6SecondaryExternalTcpMilliseconds = null,
    double Ipv6GatewayLoss60sPercent = 0, double Ipv6ExternalLoss60sPercent = 0,
    double Ipv6ExternalTcpFailure60sPercent = 0, double Ipv6SecondaryExternalLoss60sPercent = 0,
    double Ipv6SecondaryExternalTcpFailure60sPercent = 0,
    double? Ipv6GatewayJitter60sMilliseconds = null, double? Ipv6ExternalJitter60sMilliseconds = null,
    double? Ipv6ExternalTcpJitter60sMilliseconds = null);

public sealed record NetworkWindowSummary(
    double GatewayLossPercent, double ExternalLossPercent, double ExternalTcpFailurePercent,
    double? GatewayJitterMilliseconds, double? ExternalJitterMilliseconds, double? ExternalTcpJitterMilliseconds,
    double SecondaryExternalLossPercent = 0, double SecondaryExternalTcpFailurePercent = 0,
    double DnsFailurePercent = 0,
    double Ipv6GatewayLossPercent = 0, double Ipv6ExternalLossPercent = 0,
    double Ipv6ExternalTcpFailurePercent = 0, double Ipv6SecondaryExternalLossPercent = 0,
    double Ipv6SecondaryExternalTcpFailurePercent = 0,
    double? Ipv6GatewayJitterMilliseconds = null, double? Ipv6ExternalJitterMilliseconds = null,
    double? Ipv6ExternalTcpJitterMilliseconds = null);

public static class NetworkMonitorService
{
    public static async Task<string> RunAsync(TimeSpan interval, Action<NetworkMonitorSample>? onSample, CancellationToken cancellationToken)
    {
        if (interval < TimeSpan.FromSeconds(2))
            throw new ArgumentOutOfRangeException(nameof(interval), "O intervalo mínimo é de 2 segundos.");

        Directory.CreateDirectory(AppPaths.Logs);
        var file = Path.Combine(AppPaths.Logs, "network-monitor-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") +
            "-" + Guid.NewGuid().ToString("N")[..8] + ".csv");
        await using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteLineAsync(BuildCsvHeader());
        await writer.FlushAsync();
        AppLog.Write("Monitoramento contínuo de rede iniciado: " + file);
        var window = new Queue<(long Tick, NetworkMonitorSample Sample)>();

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var started = DateTimeOffset.UtcNow;
                // Re-read the route each cycle so interface changes do not pin this monitor to an old gateway.
                var gatewayAddress = GetDefaultGateway(AddressFamily.InterNetwork)?.ToString() ?? string.Empty;
                var ipv6GatewayAddress = GetDefaultGateway(AddressFamily.InterNetworkV6)?.ToString() ?? string.Empty;
                var gatewayTask = string.IsNullOrWhiteSpace(gatewayAddress)
                    ? Task.FromResult(new ProbeResult("NO_GATEWAY", null))
                    : ProbeAsync(gatewayAddress, cancellationToken);
                var externalTask = ProbeAsync("1.1.1.1", cancellationToken);
                var tcpTask = ProbeTcpAsync("1.1.1.1", 443, cancellationToken);
                var secondaryExternalTask = ProbeAsync("8.8.8.8", cancellationToken);
                var secondaryTcpTask = ProbeTcpAsync("8.8.8.8", 443, cancellationToken);
                var dnsTask = ProbeDnsAsync("cloudflare.com", cancellationToken);

                var ipv6GatewayTask = string.IsNullOrWhiteSpace(ipv6GatewayAddress)
                    ? Task.FromResult(new ProbeResult("NO_IPV6_GATEWAY", null))
                    : ProbeAsync(ipv6GatewayAddress, cancellationToken);
                const string cloudflareIpv6 = "2606:4700:4700::1111";
                const string googleIpv6 = "2001:4860:4860::8888";
                var ipv6ExternalTask = ProbeAsync(cloudflareIpv6, cancellationToken);
                var ipv6TcpTask = ProbeTcpAsync(cloudflareIpv6, 443, cancellationToken);
                var ipv6SecondaryExternalTask = ProbeAsync(googleIpv6, cancellationToken);
                var ipv6SecondaryTcpTask = ProbeTcpAsync(googleIpv6, 443, cancellationToken);
                await Task.WhenAll(gatewayTask, externalTask, tcpTask, secondaryExternalTask, secondaryTcpTask, dnsTask,
                    ipv6GatewayTask, ipv6ExternalTask, ipv6TcpTask, ipv6SecondaryExternalTask, ipv6SecondaryTcpTask);

                var raw = new NetworkMonitorSample(DateTimeOffset.Now, gatewayAddress,
                    gatewayTask.Result.Status, gatewayTask.Result.Milliseconds,
                    externalTask.Result.Status, externalTask.Result.Milliseconds,
                    tcpTask.Result.Status, tcpTask.Result.Milliseconds, 0, 0, 0, null, null, null,
                    secondaryExternalTask.Result.Status, secondaryExternalTask.Result.Milliseconds,
                    secondaryTcpTask.Result.Status, secondaryTcpTask.Result.Milliseconds,
                    dnsTask.Result.Status, dnsTask.Result.Milliseconds,
                    DnsIpv4AddressCount: dnsTask.Result.Ipv4Count,
                    DnsIpv6AddressCount: dnsTask.Result.Ipv6Count,
                    Ipv6Gateway: ipv6GatewayAddress,
                    Ipv6GatewayStatus: ipv6GatewayTask.Result.Status,
                    Ipv6GatewayMilliseconds: ipv6GatewayTask.Result.Milliseconds,
                    Ipv6ExternalStatus: ipv6ExternalTask.Result.Status,
                    Ipv6ExternalMilliseconds: ipv6ExternalTask.Result.Milliseconds,
                    Ipv6ExternalTcpStatus: ipv6TcpTask.Result.Status,
                    Ipv6ExternalTcpMilliseconds: ipv6TcpTask.Result.Milliseconds,
                    Ipv6SecondaryExternalStatus: ipv6SecondaryExternalTask.Result.Status,
                    Ipv6SecondaryExternalMilliseconds: ipv6SecondaryExternalTask.Result.Milliseconds,
                    Ipv6SecondaryExternalTcpStatus: ipv6SecondaryTcpTask.Result.Status,
                    Ipv6SecondaryExternalTcpMilliseconds: ipv6SecondaryTcpTask.Result.Milliseconds);
                var tick = System.Diagnostics.Stopwatch.GetTimestamp();
                window.Enqueue((tick, raw));
                while (window.Count > 0 &&
                    (tick - window.Peek().Tick) / (double)System.Diagnostics.Stopwatch.Frequency > 60d)
                    window.Dequeue();
                var samples = window.Select(x => x.Sample).ToArray();
                var summary = SummarizeWindow(samples);
                var sample = raw with
                {
                    GatewayLoss60sPercent = summary.GatewayLossPercent,
                    ExternalLoss60sPercent = summary.ExternalLossPercent,
                    ExternalTcpFailure60sPercent = summary.ExternalTcpFailurePercent,
                    SecondaryExternalLoss60sPercent = summary.SecondaryExternalLossPercent,
                    SecondaryExternalTcpFailure60sPercent = summary.SecondaryExternalTcpFailurePercent,
                    DnsFailure60sPercent = summary.DnsFailurePercent,
                    GatewayJitter60sMilliseconds = summary.GatewayJitterMilliseconds,
                    ExternalJitter60sMilliseconds = summary.ExternalJitterMilliseconds,
                    ExternalTcpJitter60sMilliseconds = summary.ExternalTcpJitterMilliseconds,
                    Ipv6GatewayLoss60sPercent = summary.Ipv6GatewayLossPercent,
                    Ipv6ExternalLoss60sPercent = summary.Ipv6ExternalLossPercent,
                    Ipv6ExternalTcpFailure60sPercent = summary.Ipv6ExternalTcpFailurePercent,
                    Ipv6SecondaryExternalLoss60sPercent = summary.Ipv6SecondaryExternalLossPercent,
                    Ipv6SecondaryExternalTcpFailure60sPercent = summary.Ipv6SecondaryExternalTcpFailurePercent,
                    Ipv6GatewayJitter60sMilliseconds = summary.Ipv6GatewayJitterMilliseconds,
                    Ipv6ExternalJitter60sMilliseconds = summary.Ipv6ExternalJitterMilliseconds,
                    Ipv6ExternalTcpJitter60sMilliseconds = summary.Ipv6ExternalTcpJitterMilliseconds
                };

                var rootCause = NetworkRootCauseService.Analyze(sample);
                await writer.WriteLineAsync(BuildCsvLine(sample, rootCause));
                await writer.FlushAsync();
                onSample?.Invoke(sample);

                var remaining = interval - (DateTimeOffset.UtcNow - started);
                if (remaining > TimeSpan.Zero)
                    await Task.Delay(remaining, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            AppLog.Write("Monitoramento de rede interrompido: " + file);
            return file;
        }
    }

    internal static string BuildCsvHeader() =>
        "timestamp,gateway_ipv4,gateway_ipv4_status,gateway_ipv4_ms,external_ipv4_1,external_ipv4_1_icmp_status,external_ipv4_1_icmp_ms,external_ipv4_1_tcp_status,external_ipv4_1_tcp_ms,external_ipv4_2,external_ipv4_2_icmp_status,external_ipv4_2_icmp_ms,external_ipv4_2_tcp_status,external_ipv4_2_tcp_ms,dns_host,dns_status,dns_ms,dns_a_count,dns_aaaa_count,gateway_ipv4_loss_60s_pct,external_ipv4_1_loss_60s_pct,external_ipv4_1_tcp_failure_60s_pct,external_ipv4_2_loss_60s_pct,external_ipv4_2_tcp_failure_60s_pct,dns_failure_60s_pct,gateway_ipv4_jitter_60s_ms,external_ipv4_1_jitter_60s_ms,external_ipv4_1_tcp_jitter_60s_ms,ipv6_gateway,ipv6_gateway_status,ipv6_gateway_ms,external_ipv6_1,external_ipv6_1_icmp_status,external_ipv6_1_icmp_ms,external_ipv6_1_tcp_status,external_ipv6_1_tcp_ms,external_ipv6_2,external_ipv6_2_icmp_status,external_ipv6_2_icmp_ms,external_ipv6_2_tcp_status,external_ipv6_2_tcp_ms,ipv6_gateway_loss_60s_pct,external_ipv6_1_loss_60s_pct,external_ipv6_1_tcp_failure_60s_pct,external_ipv6_2_loss_60s_pct,external_ipv6_2_tcp_failure_60s_pct,ipv6_gateway_jitter_60s_ms,external_ipv6_1_jitter_60s_ms,external_ipv6_1_tcp_jitter_60s_ms,root_cause_category,root_cause_assessment";

    internal static string BuildCsvLine(NetworkMonitorSample sample, NetworkRootCause rootCause) =>
        string.Join(",",
            sample.Timestamp.ToString("O", CultureInfo.InvariantCulture),
            Csv(sample.Gateway), Csv(sample.GatewayStatus),
            sample.GatewayMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            "1.1.1.1", Csv(sample.ExternalStatus),
            sample.ExternalMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            Csv(sample.ExternalTcpStatus),
            sample.ExternalTcpMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            "8.8.8.8", Csv(sample.SecondaryExternalStatus),
            sample.SecondaryExternalMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            Csv(sample.SecondaryExternalTcpStatus),
            sample.SecondaryExternalTcpMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            "cloudflare.com", Csv(sample.DnsStatus),
            sample.DnsMilliseconds?.ToString("0.0", CultureInfo.InvariantCulture) ?? string.Empty,
            sample.DnsIpv4AddressCount.ToString(CultureInfo.InvariantCulture),
            sample.DnsIpv6AddressCount.ToString(CultureInfo.InvariantCulture),
            sample.GatewayLoss60sPercent.ToString("0.0", CultureInfo.InvariantCulture),
            sample.ExternalLoss60sPercent.ToString("0.0", CultureInfo.InvariantCulture),
            sample.ExternalTcpFailure60sPercent.ToString("0.0", CultureInfo.InvariantCulture),
            sample.SecondaryExternalLoss60sPercent.ToString("0.0", CultureInfo.InvariantCulture),
            sample.SecondaryExternalTcpFailure60sPercent.ToString("0.0", CultureInfo.InvariantCulture),
            sample.DnsFailure60sPercent.ToString("0.0", CultureInfo.InvariantCulture),
            sample.GatewayJitter60sMilliseconds?.ToString("0.0", CultureInfo.InvariantCulture) ?? string.Empty,
            sample.ExternalJitter60sMilliseconds?.ToString("0.0", CultureInfo.InvariantCulture) ?? string.Empty,
            sample.ExternalTcpJitter60sMilliseconds?.ToString("0.0", CultureInfo.InvariantCulture) ?? string.Empty,
            Csv(sample.Ipv6Gateway), Csv(sample.Ipv6GatewayStatus),
            sample.Ipv6GatewayMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            "2606:4700:4700::1111", Csv(sample.Ipv6ExternalStatus),
            sample.Ipv6ExternalMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            Csv(sample.Ipv6ExternalTcpStatus),
            sample.Ipv6ExternalTcpMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            "2001:4860:4860::8888", Csv(sample.Ipv6SecondaryExternalStatus),
            sample.Ipv6SecondaryExternalMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            Csv(sample.Ipv6SecondaryExternalTcpStatus),
            sample.Ipv6SecondaryExternalTcpMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            sample.Ipv6GatewayLoss60sPercent.ToString("0.0", CultureInfo.InvariantCulture),
            sample.Ipv6ExternalLoss60sPercent.ToString("0.0", CultureInfo.InvariantCulture),
            sample.Ipv6ExternalTcpFailure60sPercent.ToString("0.0", CultureInfo.InvariantCulture),
            sample.Ipv6SecondaryExternalLoss60sPercent.ToString("0.0", CultureInfo.InvariantCulture),
            sample.Ipv6SecondaryExternalTcpFailure60sPercent.ToString("0.0", CultureInfo.InvariantCulture),
            sample.Ipv6GatewayJitter60sMilliseconds?.ToString("0.0", CultureInfo.InvariantCulture) ?? string.Empty,
            sample.Ipv6ExternalJitter60sMilliseconds?.ToString("0.0", CultureInfo.InvariantCulture) ?? string.Empty,
            sample.Ipv6ExternalTcpJitter60sMilliseconds?.ToString("0.0", CultureInfo.InvariantCulture) ?? string.Empty,
            Csv(rootCause.Category), Csv(rootCause.Assessment));

    public static NetworkWindowSummary SummarizeWindow(IReadOnlyList<NetworkMonitorSample> samples)
    {
        if (samples.Count == 0) return new NetworkWindowSummary(0, 0, 0, null, null, null);

        var gatewaySamples = samples.Where(x => !string.Equals(x.GatewayStatus, "NO_GATEWAY", StringComparison.OrdinalIgnoreCase)).ToArray();
        var gatewayLoss = gatewaySamples.Length == 0 ? 0 :
            100d * gatewaySamples.Count(x => !x.GatewayMilliseconds.HasValue) / gatewaySamples.Length;
        var externalLoss = 100d * samples.Count(x => !x.ExternalMilliseconds.HasValue) / samples.Count;
        var tcpFailure = 100d * samples.Count(x => !x.ExternalTcpMilliseconds.HasValue) / samples.Count;
        var secondarySamples = samples.Where(x => !string.Equals(x.SecondaryExternalStatus, "NOT_TESTED", StringComparison.OrdinalIgnoreCase)).ToArray();
        var secondaryTcpSamples = samples.Where(x => !string.Equals(x.SecondaryExternalTcpStatus, "NOT_TESTED", StringComparison.OrdinalIgnoreCase)).ToArray();
        var dnsSamples = samples.Where(x => !string.Equals(x.DnsStatus, "NOT_TESTED", StringComparison.OrdinalIgnoreCase)).ToArray();
        var secondaryLoss = secondarySamples.Length == 0 ? 0 :
            100d * secondarySamples.Count(x => !x.SecondaryExternalMilliseconds.HasValue) / secondarySamples.Length;
        var secondaryTcpFailure = secondaryTcpSamples.Length == 0 ? 0 :
            100d * secondaryTcpSamples.Count(x => !x.SecondaryExternalTcpMilliseconds.HasValue) / secondaryTcpSamples.Length;
        var dnsFailure = dnsSamples.Length == 0 ? 0 :
            100d * dnsSamples.Count(x => !string.Equals(x.DnsStatus, "OK", StringComparison.OrdinalIgnoreCase)) / dnsSamples.Length;

        var ipv6GatewaySamples = samples.Where(x =>
            !string.Equals(x.Ipv6GatewayStatus, "NOT_TESTED", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(x.Ipv6GatewayStatus, "NO_IPV6_GATEWAY", StringComparison.OrdinalIgnoreCase)).ToArray();
        var ipv6ExternalSamples = samples.Where(x => !string.Equals(x.Ipv6ExternalStatus, "NOT_TESTED", StringComparison.OrdinalIgnoreCase)).ToArray();
        var ipv6TcpSamples = samples.Where(x => !string.Equals(x.Ipv6ExternalTcpStatus, "NOT_TESTED", StringComparison.OrdinalIgnoreCase)).ToArray();
        var ipv6SecondarySamples = samples.Where(x => !string.Equals(x.Ipv6SecondaryExternalStatus, "NOT_TESTED", StringComparison.OrdinalIgnoreCase)).ToArray();
        var ipv6SecondaryTcpSamples = samples.Where(x => !string.Equals(x.Ipv6SecondaryExternalTcpStatus, "NOT_TESTED", StringComparison.OrdinalIgnoreCase)).ToArray();
        var ipv6GatewayLoss = ipv6GatewaySamples.Length == 0 ? 0 :
            100d * ipv6GatewaySamples.Count(x => !x.Ipv6GatewayMilliseconds.HasValue) / ipv6GatewaySamples.Length;
        var ipv6ExternalLoss = ipv6ExternalSamples.Length == 0 ? 0 :
            100d * ipv6ExternalSamples.Count(x => !x.Ipv6ExternalMilliseconds.HasValue) / ipv6ExternalSamples.Length;
        var ipv6TcpFailure = ipv6TcpSamples.Length == 0 ? 0 :
            100d * ipv6TcpSamples.Count(x => !x.Ipv6ExternalTcpMilliseconds.HasValue) / ipv6TcpSamples.Length;
        var ipv6SecondaryLoss = ipv6SecondarySamples.Length == 0 ? 0 :
            100d * ipv6SecondarySamples.Count(x => !x.Ipv6SecondaryExternalMilliseconds.HasValue) / ipv6SecondarySamples.Length;
        var ipv6SecondaryTcpFailure = ipv6SecondaryTcpSamples.Length == 0 ? 0 :
            100d * ipv6SecondaryTcpSamples.Count(x => !x.Ipv6SecondaryExternalTcpMilliseconds.HasValue) / ipv6SecondaryTcpSamples.Length;

        return new NetworkWindowSummary(gatewayLoss, externalLoss, tcpFailure,
            CalculateJitter(gatewaySamples.Select(x => x.GatewayMilliseconds)),
            CalculateJitter(samples.Select(x => x.ExternalMilliseconds)),
            CalculateJitter(samples.Select(x => x.ExternalTcpMilliseconds)),
            secondaryLoss, secondaryTcpFailure, dnsFailure,
            ipv6GatewayLoss, ipv6ExternalLoss, ipv6TcpFailure, ipv6SecondaryLoss, ipv6SecondaryTcpFailure,
            CalculateJitter(ipv6GatewaySamples.Select(x => x.Ipv6GatewayMilliseconds)),
            CalculateJitter(ipv6ExternalSamples.Select(x => x.Ipv6ExternalMilliseconds)),
            CalculateJitter(ipv6TcpSamples.Select(x => x.Ipv6ExternalTcpMilliseconds)));
    }

    private static double? CalculateJitter(IEnumerable<long?> values)
    {
        long? previous = null;
        var differences = new List<double>();
        foreach (var value in values)
        {
            if (!value.HasValue)
            {
                // A failed probe breaks adjacency; do not turn a multi-interval gap into "jitter".
                previous = null;
                continue;
            }
            if (previous.HasValue) differences.Add(Math.Abs((double)value.Value - previous.Value));
            previous = value.Value;
        }
        return differences.Count == 0 ? null : differences.Average();
    }

    private static async Task<ProbeResult> ProbeTcpAsync(string host, int port, CancellationToken token)
    {
        var address = IPAddress.Parse(host);
        using var client = new TcpClient(address.AddressFamily);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        var started = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await client.ConnectAsync(address, port, timeout.Token);
            started.Stop();
            return new ProbeResult("OK", started.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        {
            started.Stop();
            return new ProbeResult("TIMEOUT", null);
        }
        catch (Exception ex)
        {
            started.Stop();
            return new ProbeResult(ex.GetType().Name, null);
        }
    }

    private static async Task<ProbeResult> ProbeAsync(string host, CancellationToken token)
    {
        using var ping = new Ping();
        try
        {
            var reply = await ping.SendPingAsync(IPAddress.Parse(host), TimeSpan.FromMilliseconds(1800), new byte[32], new PingOptions(), token);
            return reply.Status == IPStatus.Success
                ? new ProbeResult("OK", reply.RoundtripTime)
                : new ProbeResult(reply.Status.ToString(), null);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) { return new ProbeResult(ex.GetType().Name, null); }
    }

    private static async Task<DnsProbeResult> ProbeDnsAsync(string host, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        var timer = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, timeout.Token);
            timer.Stop();
            var ipv4Count = addresses.Count(x => x.AddressFamily == AddressFamily.InterNetwork);
            var ipv6Count = addresses.Count(x => x.AddressFamily == AddressFamily.InterNetworkV6);
            return addresses.Length > 0
                ? new DnsProbeResult("OK", timer.Elapsed.TotalMilliseconds, ipv4Count, ipv6Count)
                : new DnsProbeResult("EMPTY", timer.Elapsed.TotalMilliseconds, 0, 0);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        {
            timer.Stop();
            return new DnsProbeResult("TIMEOUT", null, 0, 0);
        }
        catch (Exception ex)
        {
            timer.Stop();
            return new DnsProbeResult(ex.GetType().Name, null, 0, 0);
        }
    }

    private static IPAddress? GetDefaultGateway(AddressFamily addressFamily)
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up))
        {
            try
            {
                var address = nic.GetIPProperties().GatewayAddresses.Select(g => g.Address)
                    .FirstOrDefault(a =>
                        a.AddressFamily == addressFamily &&
                        (addressFamily == AddressFamily.InterNetwork
                            ? !IPAddress.Any.Equals(a)
                            : !IPAddress.IPv6Any.Equals(a)));
                if (address is not null) return address;
            }
            catch { }
        }
        return null;
    }

    private static string Csv(string value) => value.Replace(",", " ", StringComparison.Ordinal);
    private sealed record ProbeResult(string Status, long? Milliseconds);
    private sealed record DnsProbeResult(string Status, double? Milliseconds, int Ipv4Count, int Ipv6Count);
}
