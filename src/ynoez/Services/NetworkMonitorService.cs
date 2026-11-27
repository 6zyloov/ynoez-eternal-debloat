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
    double GatewayLoss60sPercent, double ExternalLoss60sPercent, double? ExternalJitter60sMilliseconds);

public static class NetworkMonitorService
{
    public static async Task<string> RunAsync(TimeSpan interval, Action<NetworkMonitorSample>? onSample, CancellationToken cancellationToken)
    {
        if (interval < TimeSpan.FromSeconds(2))
            throw new ArgumentOutOfRangeException(nameof(interval), "O intervalo mínimo é de 2 segundos.");

        Directory.CreateDirectory(AppPaths.Logs);
        var file = Path.Combine(AppPaths.Logs, "network-monitor-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv");
        var gatewayAddress = GetDefaultGateway()?.ToString() ?? string.Empty;

        await using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteLineAsync("timestamp,gateway,gateway_status,gateway_ms,external,external_status,external_ms,external_tcp_status,external_tcp_ms,gateway_loss_60s_pct,external_loss_60s_pct,external_jitter_60s_ms");
        await writer.FlushAsync();
        AppLog.Write("Monitoramento contínuo de rede iniciado: " + file);
        var window = new Queue<NetworkMonitorSample>();

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var started = DateTimeOffset.UtcNow;
                var gatewayTask = string.IsNullOrWhiteSpace(gatewayAddress)
                    ? Task.FromResult(new ProbeResult("NO_GATEWAY", null))
                    : ProbeAsync(gatewayAddress, cancellationToken);
                var externalTask = ProbeAsync("1.1.1.1", cancellationToken);
                var tcpTask = ProbeTcpAsync("1.1.1.1", 443, cancellationToken);
                await Task.WhenAll(gatewayTask, externalTask, tcpTask);

                var raw = new NetworkMonitorSample(DateTimeOffset.Now, gatewayAddress,
                    gatewayTask.Result.Status, gatewayTask.Result.Milliseconds,
                    externalTask.Result.Status, externalTask.Result.Milliseconds,
                    tcpTask.Result.Status, tcpTask.Result.Milliseconds, 0, 0, null);
                window.Enqueue(raw);
                while (window.Count > 12) window.Dequeue();
                var samples = window.ToArray();
                var gatewayLoss = 100d * samples.Count(x => x.GatewayMilliseconds is null) / samples.Length;
                var externalLoss = 100d * samples.Count(x => x.ExternalMilliseconds is null) / samples.Length;
                var jitterValues = samples.Zip(samples.Skip(1), (a, b) =>
                    a.ExternalMilliseconds.HasValue && b.ExternalMilliseconds.HasValue
                        ? Math.Abs((double)b.ExternalMilliseconds.Value - a.ExternalMilliseconds.Value)
                        : (double?)null).Where(x => x.HasValue).Select(x => x!.Value).ToArray();
                var jitter = jitterValues.Length > 0 ? jitterValues.Average() : (double?)null;
                var sample = raw with
                {
                    GatewayLoss60sPercent = gatewayLoss,
                    ExternalLoss60sPercent = externalLoss,
                    ExternalJitter60sMilliseconds = jitter
                };

                await writer.WriteLineAsync(string.Join(",",
                    sample.Timestamp.ToString("O", CultureInfo.InvariantCulture),
                    Csv(sample.Gateway), Csv(sample.GatewayStatus),
                    sample.GatewayMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    "1.1.1.1", Csv(sample.ExternalStatus),
                    sample.ExternalMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    Csv(sample.ExternalTcpStatus),
                    sample.ExternalTcpMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    sample.GatewayLoss60sPercent.ToString("0.0", CultureInfo.InvariantCulture),
                    sample.ExternalLoss60sPercent.ToString("0.0", CultureInfo.InvariantCulture),
                    sample.ExternalJitter60sMilliseconds?.ToString("0.0", CultureInfo.InvariantCulture) ?? string.Empty));
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
}
