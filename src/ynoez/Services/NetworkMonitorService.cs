using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace ynoez.Services;

public sealed record NetworkMonitorSample(
    DateTimeOffset Timestamp, string Gateway, string GatewayStatus, long? GatewayMilliseconds,
    string ExternalStatus, long? ExternalMilliseconds);

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
        await writer.WriteLineAsync("timestamp,gateway,gateway_status,gateway_ms,external,external_status,external_ms");
        await writer.FlushAsync();
        AppLog.Write("Monitoramento contínuo de rede iniciado: " + file);

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
                await Task.WhenAll(gatewayTask, externalTask);

                var sample = new NetworkMonitorSample(DateTimeOffset.Now, gatewayAddress,
                    gatewayTask.Result.Status, gatewayTask.Result.Milliseconds,
                    externalTask.Result.Status, externalTask.Result.Milliseconds);

                await writer.WriteLineAsync(string.Join(",",
                    sample.Timestamp.ToString("O", CultureInfo.InvariantCulture),
                    Csv(sample.Gateway), Csv(sample.GatewayStatus),
                    sample.GatewayMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    "1.1.1.1", Csv(sample.ExternalStatus),
                    sample.ExternalMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty));
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
