using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

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

        progress?.Report("Verificando resolução DNS…");
        var sw = Stopwatch.StartNew();
        try
        {
            var addresses = await Dns.GetHostAddressesAsync("cloudflare.com", cancellationToken);
            sw.Stop();
            lines.Add($"DNS: OK • {sw.Elapsed.TotalMilliseconds:0.0} ms • {addresses.Length} endereço(s) retornado(s)");
        }
        catch (Exception ex)
        {
            sw.Stop();
            lines.Add("DNS: falha • " + ex.Message);
        }

        lines.Add("");
        lines.Add("LEITURA");
        lines.Add("Perda no gateway aponta para Wi-Fi/Ethernet, adaptador ou roteador. Perda apenas no teste externo pode estar na rota ou no provedor.");
        lines.Add("Os testes são diagnósticos; não alteram DNS, MTU, IPv6, firewall ou configurações do adaptador.");
        AppLog.Write("Diagnóstico de rede concluído.");
        progress?.Report("Diagnóstico concluído.");
        return string.Join(Environment.NewLine, lines);
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
