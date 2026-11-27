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

        if (hasGateway && gatewayLoss >= LossThresholdPercent)
            return new NetworkRootCause("REDE LOCAL",
                "Há perda recorrente até o gateway. Investigue sinal Wi-Fi, cabo, adaptador, interferência ou roteador antes de atribuir a falha ao provedor.");

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
