using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace ynoez.Services;

public sealed record PingSummary(string Target, int Sent, int Received, double LossPercent, double AverageMs, double MinimumMs, double MaximumMs, double JitterMs)
{
    public string ToDisplay() => $"{Target}: {AverageMs:0.0} ms médio | mín {MinimumMs:0.0} | máx {MaximumMs:0.0} | jitter {JitterMs:0.0} ms | perda {LossPercent:0.#}% ({Received}/{Sent})";
}

public static class NetworkDiagnosticService
{
    public static async Task<string> RunAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var lines = new List<string> { $"Diagnóstico iniciado: {DateTime.Now:dd/MM/yyyy HH:mm:ss}", "" };
        var gateway = GetDefaultGateway();
        if (gateway is not null)
        {
            progress?.Report("Testando o roteador/rede local…");
            var local = await TestHostAsync(gateway.ToString(), 8, progress, cancellationToken);
            lines.Add("REDE LOCAL");
            lines.Add(local.ToDisplay());
        }
        else
        {
            lines.Add("REDE LOCAL");
            lines.Add("Gateway padrão não encontrado. Verifique se existe um adaptador conectado.");
        }

        progress?.Report("Testando conectividade externa…");
        var internet = await TestHostAsync("1.1.1.1", 10, progress, cancellationToken);
        lines.Add("");
        lines.Add("CONECTIVIDADE EXTERNA (Cloudflare DNS)");
        lines.Add(internet.ToDisplay());

        progress?.Report("Testando conectividade TCP…");
        lines.Add("");
        lines.Add("CONECTIVIDADE TCP (HTTPS/443)");
        lines.Add(await TestTcpAsync("1.1.1.1", 443, cancellationToken));
        lines.Add(await TestTcpAsync("8.8.8.8", 443, cancellationToken));

        progress?.Report("Verificando resolução DNS…");
        lines.Add("");
        lines.Add("RESOLUÇÃO DNS");
        lines.Add(await TestDnsAsync("cloudflare.com", cancellationToken));
        lines.Add(await TestDnsAsync("google.com", cancellationToken));

        progress?.Report("Rastreando rota até a internet…");
        lines.Add("");
        lines.Add("ROTA ATÉ 1.1.1.1");
        try
        {
            var route = await RunTraceRouteAsync(cancellationToken);
            lines.Add(string.IsNullOrWhiteSpace(route) ? "tracert não retornou dados." : route);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { lines.Add("Não foi possível rastrear a rota: " + ex.Message); }

        lines.Add("");
        lines.Add("LEITURA");
        lines.Add("Perda no gateway aponta para Wi-Fi/Ethernet, adaptador ou roteador. Perda apenas no teste externo pode estar na rota ou no provedor.");
        lines.Add("Os testes são diagnósticos; não alteram DNS, MTU, IPv6, firewall ou configurações do adaptador.");
        AppLog.Write("Diagnóstico de rede concluído.");
        progress?.Report("Diagnóstico concluído.");
        return string.Join(Environment.NewLine, lines);
    }

    private static async Task<string> TestTcpAsync(string host, int port, CancellationToken token)
    {
        using var client = new TcpClient();
        var sw = Stopwatch.StartNew();
        try
        {
            await client.ConnectAsync(host, port, token);
            sw.Stop();
            return host + ":" + port + " • TCP OK • " + sw.Elapsed.TotalMilliseconds.ToString("0.0") + " ms";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            sw.Stop();
            return host + ":" + port + " • TCP falhou • " + ex.GetType().Name;
        }
    }

    private static async Task<string> TestDnsAsync(string host, CancellationToken token)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, token);
            sw.Stop();
            var ipv4 = addresses.Count(x => x.AddressFamily == AddressFamily.InterNetwork);
            var ipv6 = addresses.Count(x => x.AddressFamily == AddressFamily.InterNetworkV6);
            return host + " • OK • " + sw.Elapsed.TotalMilliseconds.ToString("0.0") + " ms • IPv4 " + ipv4 + " / IPv6 " + ipv6;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            sw.Stop();
            return host + " • falha • " + ex.GetType().Name;
        }
    }

    private static async Task<string> RunTraceRouteAsync(CancellationToken token)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("tracert.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        foreach (var arg in new[] { "-d", "-h", "8", "-w", "400", "1.1.1.1" })
            process.StartInfo.ArgumentList.Add(arg);

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return "Rota excedeu o limite de 15 segundos.";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        var output = await outputTask;
        var error = await errorTask;
        return process.ExitCode == 0 ? output.Trim() : (output + Environment.NewLine + error).Trim();
    }

    private static async Task<PingSummary> TestHostAsync(string host, int count, IProgress<string>? progress, CancellationToken token)
    {
        var samples = new List<double>();
        using var ping = new Ping();
        for (var i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var reply = await ping.SendPingAsync(IPAddress.Parse(host), TimeSpan.FromMilliseconds(1500), new byte[32], new PingOptions(), token);
                if (reply.Status == IPStatus.Success) samples.Add(reply.RoundtripTime);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch { }
            progress?.Report($"Testando {host}: {i + 1}/{count}");
        }
        var avg = samples.Count == 0 ? 0 : samples.Average();
        var jitter = samples.Count < 2 ? 0 : samples.Zip(samples.Skip(1), (a, b) => Math.Abs(a - b)).Average();
        return new PingSummary(host, count, samples.Count, 100d * (count - samples.Count) / count, avg,
            samples.Count == 0 ? 0 : samples.Min(), samples.Count == 0 ? 0 : samples.Max(), jitter);
    }

    private static IPAddress? GetDefaultGateway()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up))
        {
            try
            {
                var address = nic.GetIPProperties().GatewayAddresses
                    .Select(g => g.Address)
                    .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.Any.Equals(a));
                if (address is not null) return address;
            }
            catch { }
        }
        return null;
    }
}
