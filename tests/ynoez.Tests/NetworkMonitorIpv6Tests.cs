using Xunit;
using ynoez.Services;

namespace ynoez.Tests;

public sealed class NetworkMonitorIpv6Tests
{
    private static NetworkMonitorSample Sample(
        string ipv6GatewayStatus = "OK",
        long? ipv6GatewayMs = 2,
        string ipv6Status = "OK",
        long? ipv6Ms = 20,
        string ipv6TcpStatus = "OK",
        long? ipv6TcpMs = 25,
        string ipv6SecondaryStatus = "OK",
        long? ipv6SecondaryMs = 22,
        string ipv6SecondaryTcpStatus = "OK",
        long? ipv6SecondaryTcpMs = 27,
        double ipv6GatewayLoss = 0,
        double ipv6Loss = 0,
        double ipv6TcpFailure = 0,
        double ipv6SecondaryLoss = 0,
        double ipv6SecondaryTcpFailure = 0)
    {
        return new NetworkMonitorSample(
            DateTimeOffset.UtcNow,
            "192.168.1.1", "OK", 2,
            "OK", 20, "OK", 25,
            0, 0, 0, 0, 0, 0,
            "OK", 22, "OK", 27,
            "OK", 8.5, 0, 0, 0,
            DnsIpv4AddressCount: 2,
            DnsIpv6AddressCount: 2,
            Ipv6Gateway: ipv6GatewayStatus == "NO_IPV6_GATEWAY" ? "" : "fe80::1%12",
            Ipv6GatewayStatus: ipv6GatewayStatus,
            Ipv6GatewayMilliseconds: ipv6GatewayMs,
            Ipv6ExternalStatus: ipv6Status,
            Ipv6ExternalMilliseconds: ipv6Ms,
            Ipv6ExternalTcpStatus: ipv6TcpStatus,
            Ipv6ExternalTcpMilliseconds: ipv6TcpMs,
            Ipv6SecondaryExternalStatus: ipv6SecondaryStatus,
            Ipv6SecondaryExternalMilliseconds: ipv6SecondaryMs,
            Ipv6SecondaryExternalTcpStatus: ipv6SecondaryTcpStatus,
            Ipv6SecondaryExternalTcpMilliseconds: ipv6SecondaryTcpMs,
            Ipv6GatewayLoss60sPercent: ipv6GatewayLoss,
            Ipv6ExternalLoss60sPercent: ipv6Loss,
            Ipv6ExternalTcpFailure60sPercent: ipv6TcpFailure,
            Ipv6SecondaryExternalLoss60sPercent: ipv6SecondaryLoss,
            Ipv6SecondaryExternalTcpFailure60sPercent: ipv6SecondaryTcpFailure);
    }

    [Fact]
    public void SummarizeWindow_CalculatesIpv6LossAndJitterSeparatelyFromIpv4()
    {
        var samples = new[]
        {
            Sample(ipv6GatewayMs: 2, ipv6Ms: 10, ipv6TcpMs: 20),
            Sample(ipv6GatewayMs: 4, ipv6Ms: 14, ipv6TcpMs: 24),
            Sample(ipv6GatewayMs: null, ipv6Ms: null, ipv6TcpMs: null,
                ipv6SecondaryMs: null, ipv6SecondaryTcpMs: null),
            Sample(ipv6GatewayMs: null, ipv6Ms: null, ipv6TcpMs: null,
                ipv6SecondaryMs: null, ipv6SecondaryTcpMs: null)
        };

        var summary = NetworkMonitorService.SummarizeWindow(samples);

        Assert.Equal(0, summary.GatewayLossPercent);
        Assert.Equal(0, summary.ExternalLossPercent);
        Assert.Equal(50, summary.Ipv6GatewayLossPercent);
        Assert.Equal(50, summary.Ipv6ExternalLossPercent);
        Assert.Equal(50, summary.Ipv6ExternalTcpFailurePercent);
        Assert.Equal(50, summary.Ipv6SecondaryExternalLossPercent);
        Assert.Equal(50, summary.Ipv6SecondaryExternalTcpFailurePercent);
        Assert.NotNull(summary.Ipv6ExternalJitterMilliseconds);
    }

    [Fact]
    public void Analyze_ReportsIpv6UnavailableWhenIpv4WorksAndBothIpv6DestinationsFail()
    {
        var sample = Sample(
            ipv6GatewayStatus: "NO_IPV6_GATEWAY",
            ipv6GatewayMs: null,
            ipv6Status: "TIMEOUT",
            ipv6Ms: null,
            ipv6TcpStatus: "TIMEOUT",
            ipv6TcpMs: null,
            ipv6SecondaryStatus: "TIMEOUT",
            ipv6SecondaryMs: null,
            ipv6SecondaryTcpStatus: "TIMEOUT",
            ipv6SecondaryTcpMs: null,
            ipv6Loss: 100,
            ipv6TcpFailure: 100,
            ipv6SecondaryLoss: 100,
            ipv6SecondaryTcpFailure: 100);

        var result = NetworkRootCauseService.Analyze(sample);

        Assert.Equal("IPv6 / SEM CONECTIVIDADE", result.Category);
        Assert.Contains("IPv4 continua acessível", result.Assessment);
        Assert.Contains("Nenhum gateway IPv6", result.Assessment);
    }

    [Fact]
    public void Analyze_DistinguishesIpv6IcmpFilteringFromIpv6TcpFailure()
    {
        var icmpFiltered = Sample(
            ipv6Status: "TIMEOUT",
            ipv6Ms: null,
            ipv6SecondaryStatus: "TIMEOUT",
            ipv6SecondaryMs: null,
            ipv6Loss: 100,
            ipv6SecondaryLoss: 100);
        var tcpFailed = Sample(
            ipv6TcpStatus: "TIMEOUT",
            ipv6TcpMs: null,
            ipv6SecondaryTcpStatus: "TIMEOUT",
            ipv6SecondaryTcpMs: null,
            ipv6TcpFailure: 100,
            ipv6SecondaryTcpFailure: 100);

        Assert.Equal("IPv6 ICMP / FILTRAGEM", NetworkRootCauseService.Analyze(icmpFiltered).Category);
        Assert.Equal("IPv6 TCP / SERVIÇO", NetworkRootCauseService.Analyze(tcpFailed).Category);
    }

    [Fact]
    public void Analyze_DetectsLossToTheLocalIpv6Gateway()
    {
        var sample = Sample(ipv6GatewayLoss: 25);

        var result = NetworkRootCauseService.Analyze(sample);

        Assert.Equal("REDE LOCAL IPv6", result.Category);
        Assert.Contains("gateway IPv6", result.Assessment);
    }

    [Fact]
    public void BuildCsvLine_PersistsIpv6MetricsAndDnsAddressFamilyCounts()
    {
        var sample = Sample();
        var assessment = NetworkRootCauseService.Analyze(sample);

        var line = NetworkMonitorService.BuildCsvLine(sample, assessment);

        var headerColumns = NetworkMonitorService.BuildCsvHeader().Split(',');
        var lineColumns = line.Split(',');
        Assert.Equal(headerColumns.Length, lineColumns.Length);
        Assert.Equal("dns_a_count", headerColumns[17]);
        Assert.Equal("dns_aaaa_count", headerColumns[18]);
        Assert.Equal("2", lineColumns[17]);
        Assert.Equal("2", lineColumns[18]);
        Assert.Equal("fe80::1%12", lineColumns[28]);
        Assert.Equal("2606:4700:4700::1111", lineColumns[31]);
        Assert.Equal("2001:4860:4860::8888", lineColumns[36]);
    }

    [Fact]
    public void LegacySamplesWithoutIpv6Data_DoNotTriggerIpv6RootCause()
    {
        var legacy = new NetworkMonitorSample(
            DateTimeOffset.UtcNow, "192.168.1.1", "OK", 1,
            "OK", 20, "OK", 25,
            0, 0, 0, null, null, null);

        var result = NetworkRootCauseService.Analyze(legacy);

        Assert.Equal("SEM FALHA RECORRENTE DETECTADA", result.Category);
    }
}
