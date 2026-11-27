namespace ynoez.Services;

public sealed record NetworkRootCause(string Category, string Assessment);

public static class NetworkRootCauseService
{
    private const double LossThresholdPercent = 10d;

    public static NetworkRootCause Analyze(NetworkMonitorSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        var hasGateway = !string.IsNullOrWhiteSpace(sample.Gateway) &&
            !string.Equals(sample.GatewayStatus, "NO_GATEWAY", StringComparison.OrdinalIgnoreCase);
        var gatewayLoss = sample.GatewayLoss60sPercent;
        var firstLoss = sample.ExternalLoss60sPercent;
        var secondLoss = sample.SecondaryExternalLoss60sPercent;
        var firstTcpFailure = sample.ExternalTcpFailure60sPercent;
        var secondTcpFailure = sample.SecondaryExternalTcpFailure60sPercent;
        var dnsFailure = sample.DnsFailure60sPercent;
        var ipv6GatewayTested = !string.Equals(sample.Ipv6GatewayStatus, "NOT_TESTED", StringComparison.OrdinalIgnoreCase);
        var ipv6GatewayAvailable = ipv6GatewayTested &&
            !string.Equals(sample.Ipv6GatewayStatus, "NO_IPV6_GATEWAY", StringComparison.OrdinalIgnoreCase);
        var ipv6GatewayLoss = sample.Ipv6GatewayLoss60sPercent;
        var ipv6FirstIcmpLoss = sample.Ipv6ExternalLoss60sPercent;
        var ipv6SecondIcmpLoss = sample.Ipv6SecondaryExternalLoss60sPercent;
        var ipv6FirstTcpFailure = sample.Ipv6ExternalTcpFailure60sPercent;
        var ipv6SecondTcpFailure = sample.Ipv6SecondaryExternalTcpFailure60sPercent;
        var ipv6Tested = !string.Equals(sample.Ipv6ExternalStatus, "NOT_TESTED", StringComparison.OrdinalIgnoreCase);
        var ipv6GatewayHasLoss = ipv6GatewayAvailable && ipv6GatewayLoss >= LossThresholdPercent;
        var ipv4GatewayHasLoss = hasGateway && gatewayLoss >= LossThresholdPercent;
        var ipv4TcpHealthy = firstTcpFailure < LossThresholdPercent && secondTcpFailure < LossThresholdPercent;

        if (ipv6GatewayHasLoss && ipv4GatewayHasLoss)
            return new NetworkRootCause("REDE LOCAL IPv4/IPv6",
                "Há perda recorrente até os gateways IPv4 e IPv6. Investigue primeiro o adaptador, cabo/Wi-Fi, sinal e roteador; os testes públicos não isolam a causa enquanto ambos os gateways locais apresentam perda.");

        if (ipv4GatewayHasLoss)
            return new NetworkRootCause("REDE LOCAL",
                "Há perda recorrente até o gateway IPv4. Investigue sinal Wi-Fi, cabo, adaptador ou roteador antes de atribuir a falha ao provedor. A análise IPv6 aparece separadamente nos dados do monitor.");

        if (ipv6GatewayHasLoss)
            return new NetworkRootCause("REDE LOCAL IPv6",
                "Há perda recorrente até o gateway IPv6. Verifique RA/SLAAC, Wi-Fi/Ethernet, roteador e conectividade IPv6 local; os testes IPv4 são avaliados separadamente.");

        if (ipv6Tested && ipv4TcpHealthy &&
            ipv6FirstIcmpLoss >= LossThresholdPercent && ipv6SecondIcmpLoss >= LossThresholdPercent &&
            ipv6FirstTcpFailure >= LossThresholdPercent && ipv6SecondTcpFailure >= LossThresholdPercent)
        {
            var noGatewayMessage = ipv6GatewayTested && !ipv6GatewayAvailable
                ? " Nenhum gateway IPv6 foi reportado pelo Windows nesta amostra."
                : "";
            return new NetworkRootCause("IPv6 / SEM CONECTIVIDADE",
                "TCP/443 IPv4 continua acessível, mas dois destinos IPv6 falham por ICMP e TCP. Isso sugere ausência de rota IPv6, IPv6 desativado/sem delegação ou filtragem no roteador/provedor; não indica queda geral da Internet." + noGatewayMessage);
        }

        if (ipv6Tested &&
            (ipv6FirstIcmpLoss >= LossThresholdPercent || ipv6SecondIcmpLoss >= LossThresholdPercent) &&
            ipv6FirstTcpFailure < LossThresholdPercent && ipv6SecondTcpFailure < LossThresholdPercent)
            return new NetworkRootCause("IPv6 ICMP / FILTRAGEM",
                "Os probes ICMP IPv6 falham, mas TCP/443 IPv6 continua respondendo. Isso pode indicar filtragem de ICMP, não necessariamente perda real nos aplicativos.");

        if (ipv6Tested &&
            (ipv6FirstTcpFailure >= LossThresholdPercent || ipv6SecondTcpFailure >= LossThresholdPercent) &&
            ipv6FirstIcmpLoss < LossThresholdPercent && ipv6SecondIcmpLoss < LossThresholdPercent)
            return new NetworkRootCause("IPv6 TCP / SERVIÇO",
                "ICMP IPv6 responde, mas TCP/443 falha em pelo menos um destino. Verifique filtragem TCP/443, firewall, proxy ou rota específica do destino.");

        if (dnsFailure >= LossThresholdPercent &&
            firstLoss < LossThresholdPercent && secondLoss < LossThresholdPercent &&
            firstTcpFailure < LossThresholdPercent && secondTcpFailure < LossThresholdPercent)
            return new NetworkRootCause("DNS",
                "Os dois destinos IP e TCP continuam acessíveis, mas a resolução de nomes falha. Revise o DNS recebido do roteador/provedor ou o resolvedor configurado.");

        if (firstLoss >= LossThresholdPercent && secondLoss >= LossThresholdPercent &&
            firstTcpFailure >= LossThresholdPercent && secondTcpFailure >= LossThresholdPercent)
            return new NetworkRootCause(hasGateway ? "ROTA / PROVEDOR" : "CONECTIVIDADE EXTERNA",
                "Há falha simultânea em dois destinos públicos por ICMP e TCP. Se o gateway local responde, o problema provavelmente está depois da rede local (rota, modem ou provedor).");

        if ((firstLoss >= LossThresholdPercent || secondLoss >= LossThresholdPercent) &&
            firstTcpFailure < LossThresholdPercent && secondTcpFailure < LossThresholdPercent)
            return new NetworkRootCause("ICMP / FILTRAGEM",
                "Há falha de ping, mas TCP/443 continua respondendo. Isso pode indicar filtragem ou limitação de ICMP, não necessariamente perda real nos aplicativos.");

        if ((firstLoss >= LossThresholdPercent) != (secondLoss >= LossThresholdPercent) ||
            (firstTcpFailure >= LossThresholdPercent) != (secondTcpFailure >= LossThresholdPercent))
            return new NetworkRootCause("DESTINO / ROTA ESPECÍFICA",
                "Os resultados divergem entre Cloudflare e Google. Pode ser rota ou política específica do destino; compare também DNS, gateway e aplicações afetadas.");

        if ((firstTcpFailure >= LossThresholdPercent || secondTcpFailure >= LossThresholdPercent) &&
            firstLoss < LossThresholdPercent && secondLoss < LossThresholdPercent)
            return new NetworkRootCause("TCP / SERVIÇO",
                "Os pings respondem, mas conexões TCP/443 falham. Verifique firewall, filtragem do roteador, proxy ou rota TCP.");

        if (!hasGateway)
            return new NetworkRootCause("SEM GATEWAY",
                "O Windows não informou um gateway IPv4 nesta amostra. A comparação da rede local está incompleta; pode haver conexão sem gateway IPv4 ou somente IPv6.");

        return new NetworkRootCause("SEM FALHA RECORRENTE DETECTADA",
            "Nesta janela de 60 segundos não apareceu perda recorrente acima do limite nos testes. Isso não descarta quedas raras ou problemas que ocorram fora da janela.");
    }
}
