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
    double DnsFailure60sPercent = 0);

public sealed record NetworkWindowSummary(
    double GatewayLossPercent, double ExternalLossPercent, double ExternalTcpFailurePercent,
    double? GatewayJitterMilliseconds, double? ExternalJitterMilliseconds, double? ExternalTcpJitterMilliseconds,
    double SecondaryExternalLossPercent = 0, double SecondaryExternalTcpFailurePercent = 0,
    double DnsFailurePercent = 0);

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
        await writer.WriteLineAsync("timestamp,gateway,gateway_status,gateway_ms,external_1,external_1_icmp_status,external_1_icmp_ms,external_1_tcp_status,external_1_tcp_ms,external_2,external_2_icmp_status,external_2_icmp_ms,external_2_tcp_status,external_2_tcp_ms,dns_host,dns_status,dns_ms,gateway_loss_60s_pct,external_1_loss_60s_pct,external_1_tcp_failure_60s_pct,external_2_loss_60s_pct,external_2_tcp_failure_60s_pct,dns_failure_60s_pct,gateway_jitter_60s_ms,external_1_jitter_60s_ms,external_1_tcp_jitter_60s_ms");
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
                var gatewayAddress = GetDefaultGateway()?.ToString() ?? string.Empty;
                var gatewayTask = string.IsNullOrWhiteSpace(gatewayAddress)
                    ? Task.FromResult(new ProbeResult("NO_GATEWAY", null))
                    : ProbeAsync(gatewayAddress, cancellationToken);
                var externalTask = ProbeAsync("1.1.1.1", cancellationToken);
                var tcpTask = ProbeTcpAsync("1.1.1.1", 443, cancellationToken);
                var secondaryExternalTask = ProbeAsync("8.8.8.8", cancellationToken);
                var secondaryTcpTask = ProbeTcpAsync("8.8.8.8", 443, cancellationToken);
                var dnsTask = ProbeDnsAsync("cloudflare.com", cancellationToken);
                await Task.WhenAll(gatewayTask, externalTask, tcpTask, secondaryExternalTask, secondaryTcpTask, dnsTask);

                var raw = new NetworkMonitorSample(DateTimeOffset.Now, gatewayAddress,
                    gatewayTask.Result.Status, gatewayTask.Result.Milliseconds,
                    externalTask.Result.Status, externalTask.Result.Milliseconds,
                    tcpTask.Result.Status, tcpTask.Result.Milliseconds, 0, 0, 0, null, null, null,
                    secondaryExternalTask.Result.Status, secondaryExternalTask.Result.Milliseconds,
                    secondaryTcpTask.Result.Status, secondaryTcpTask.Result.Milliseconds,
                    dnsTask.Result.Status, dnsTask.Result.Milliseconds);
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
                    ExternalTcpJitter60sMilliseconds = summary.ExternalTcpJitterMilliseconds
                };

                await writer.WriteLineAsync(string.Join(",",
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
                    sample.GatewayLoss60sPercent.ToString("0.0", CultureInfo.InvariantCulture),
                    sample.ExternalLoss60sPercent.ToString("0.0", CultureInfo.InvariantCulture),
                    sample.ExternalTcpFailure60sPercent.ToString("0.0", CultureInfo.InvariantCulture),
                    sample.SecondaryExternalLoss60sPercent.ToString("0.0", CultureInfo.InvariantCulture),
                    sample.SecondaryExternalTcpFailure60sPercent.ToString("0.0", CultureInfo.InvariantCulture),
                    sample.DnsFailure60sPercent.ToString("0.0", CultureInfo.InvariantCulture),
                    sample.GatewayJitter60sMilliseconds?.ToString("0.0", CultureInfo.InvariantCulture) ?? string.Empty,
                    sample.ExternalJitter60sMilliseconds?.ToString("0.0", CultureInfo.InvariantCulture) ?? string.Empty,
                    sample.ExternalTcpJitter60sMilliseconds?.ToString("0.0", CultureInfo.InvariantCulture) ?? string.Empty));
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
        return new NetworkWindowSummary(gatewayLoss, externalLoss, tcpFailure,
            CalculateJitter(gatewaySamples.Select(x => x.GatewayMilliseconds)),
            CalculateJitter(samples.Select(x => x.ExternalMilliseconds)),
            CalculateJitter(samples.Select(x => x.ExternalTcpMilliseconds)),
            secondaryLoss, secondaryTcpFailure, dnsFailure);
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
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        var started = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await client.ConnectAsync(IPAddress.Parse(host), port, timeout.Token);
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
            return addresses.Length > 0
                ? new DnsProbeResult("OK", timer.Elapsed.TotalMilliseconds)
                : new DnsProbeResult("EMPTY", timer.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        {
            timer.Stop();
            return new DnsProbeResult("TIMEOUT", null);
        }
        catch (Exception ex)
        {
            timer.Stop();
            return new DnsProbeResult(ex.GetType().Name, null);
        }
    }

    private static IPAddress? GetDefaultGateway()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up))
        {
            try
            {
                var address = nic.GetIPProperties().GatewayAddresses.Select(g => g.Address)
                    .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.Any.Equals(a));
                if (address is not null) return address;
            }
            catch { }
        }
        return null;
    }

    private static string Csv(string value) => value.Replace(",", " ", StringComparison.Ordinal);
    private sealed record ProbeResult(string Status, long? Milliseconds);
    private sealed record DnsProbeResult(string Status, double? Milliseconds);
}
