# Diagnóstico e monitoramento de rede 0.2.2

## Diagnóstico sob demanda
- ICMP para gateway e 1.1.1.1, com latência, perda e jitter.
- TCP connect para 1.1.1.1:443 e 8.8.8.8:443, com timeout limitado.
- Resolução DNS de cloudflare.com e google.com, com contagem IPv4/IPv6.
- tracert -d com limite de hops e tempo total.
- Nenhuma configuração de rede é alterada pelo diagnóstico.

## Monitor contínuo
- Intervalo nominal de 5 segundos.
- ICMP para gateway e 1.1.1.1; TCP 1.1.1.1:443.
- Janela móvel de até 12 amostras (~60 segundos) para perda e jitter.
- CSV em Logs com timestamp, estados, latências, perda e jitter.
- Jitter é a média das diferenças absolutas entre RTTs externos de amostras consecutivas bem-sucedidas.
- O monitor diferencia falha ICMP de TCP, mas não prova sozinho a origem de uma queda.

## Energia do adaptador
- Consulta somente adaptadores físicos e recursos Selective Suspend / Device Sleep on Disconnect suportados pelo driver.
- Desativa somente recursos que aparecem como Enabled, com backup por adaptador e verificação.
- Usa Set-NetAdapterPowerManagement com NoRestart; a aplicação pode exigir reconexão ou reinicialização.
- Não mexe em EEE, interrupt moderation, driver, MTU, DNS, IPv6 ou propriedades avançadas ainda.
- Pode aumentar consumo; não garante estabilidade quando a causa está no roteador/ISP.
