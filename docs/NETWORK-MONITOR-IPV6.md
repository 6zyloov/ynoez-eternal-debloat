# Monitoramento contínuo IPv4 e IPv6

## Probes por ciclo

A cada cinco segundos, o monitor consulta em paralelo:

| Família | Rede local | Destino Cloudflare | Destino Google |
|---|---|---|---|
| IPv4 | Gateway IPv4 encontrado no Windows (ICMP) | 1.1.1.1 (ICMP e TCP/443) | 8.8.8.8 (ICMP e TCP/443) |
| IPv6 | Gateway IPv6 reportado na interface (ICMP) | 2606:4700:4700::1111 (ICMP e TCP/443) | 2001:4860:4860::8888 (ICMP e TCP/443) |

Também executa uma resolução local de `cloudflare.com`, informando o tempo e quantos endereços IPv4 (A) e IPv6 (AAAA) foram retornados. Essa contagem descreve respostas DNS; não significa que o transporte da consulta DNS tenha usado IPv4 ou IPv6.

Os endereços públicos usados são os resolvedores IPv6 publicados oficialmente por [Cloudflare](https://developers.cloudflare.com/1.1.1.1/ip-addresses/) e [Google Public DNS](https://developers.google.com/speed/public-dns/docs/using).

## Estatísticas

A janela móvel considera os últimos 60 segundos. Perda e jitter são calculados separadamente para os probes IPv4 e IPv6 e para ICMP/TCP. Falha de probe interrompe a adjacência no cálculo de jitter; o monitor não transforma um intervalo sem resposta em jitter artificial.

O CSV inclui endereço/estado/latência de cada probe, gateway das duas famílias, contagem A/AAAA, percentuais de perda/falha, jitter e hipótese de causa.

## Interpretação

- **REDE LOCAL / REDE LOCAL IPv6:** perda recorrente até o gateway da família correspondente.
- **IPv6 / SEM CONECTIVIDADE:** IPv4 permanece saudável enquanto dois destinos IPv6 falham em ICMP e TCP. Pode significar ausência de rota IPv6, IPv6 desativado/sem delegação ou filtragem; não significa queda geral da Internet.
- **IPv6 ICMP / FILTRAGEM:** ping IPv6 falha, mas TCP/443 IPv6 responde.
- **IPv6 TCP / SERVIÇO:** ICMP IPv6 responde, mas TCP/443 falha.
- **DNS:** os destinos IP e TCP respondem, mas a resolução de nomes falha.

A hipótese não é prova de causa; o tráfego pode ser filtrado por destino, firewall, roteador ou provedor. Os probes IPv6 adicionais podem não responder em uma rede sem conectividade IPv6. O ynoez não altera DNS, rotas, IPv6, firewall, MTU ou propriedades de adaptador.

## Limitações

- O teste TCP verifica o handshake TCP/443, não a validação TLS nem a qualidade de um serviço HTTP.
- O ping pode ser filtrado; a classificação sempre compara ICMP e TCP.
- O gateway IPv6 é o que o Windows reporta por interface; algumas topologias não publicam gateway IPv6 na mesma forma que o IPv4.
- O monitor é snapshot por ciclo; quedas raras podem não coincidir com a janela móvel.
