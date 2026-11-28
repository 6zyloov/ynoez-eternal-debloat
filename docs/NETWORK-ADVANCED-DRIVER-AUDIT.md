# Auditoria avançada de adaptadores de rede

## Escopo

A página Network pode consultar as propriedades avançadas expostas pelos drivers de adaptadores físicos. A leitura usa os cmdlets `Get-NetAdapter -Physical` e `Get-NetAdapterAdvancedProperty -AllProperties`, sem UAC e sem chamar comandos de configuração. O relatório inclui nome do adaptador, descrição, nome/valor visível, palavra-chave e valor de registro reportados pelo driver.

O recurso é somente leitura. Não usa `Set-NetAdapterAdvancedProperty`, não reinicia interfaces e não altera Registro. Propriedades não publicadas pelo driver não aparecem; nomes, opções e valores variam por fabricante, modelo, revisão e versão do driver.

## Como interpretar

- **Interrupt Moderation:** reduz a frequência de interrupções ao agrupar pacotes; isso troca uso de CPU por latência de processamento. Para latência muito baixa, a desativação pode ser testada em cenário controlado, mas não é um padrão universal.
- **EEE / Green Ethernet:** recursos de economia de energia do link. Não devem ser desligados automaticamente; investigue primeiro cabo, porta, roteador, eventos e renegociação.
- **RSS:** distribui processamento de recepção entre CPUs compatíveis. Em geral, mantenha habilitado quando suportado, mas confirme as capacidades e a topologia reais.
- **LSO / Checksum Offload:** transferem tarefas de rede para a NIC. Normalmente devem seguir o padrão do driver; uma exceção exige incompatibilidade demonstrada.
- **Speed & Duplex:** negociação automática costuma ser a escolha mais compatível. Forçar velocidade/duplex sem configurar a outra ponta pode causar problemas.
- **Flow Control e buffers:** dependem do adaptador e do equipamento conectado. Aumentar buffers ou mudar Flow Control sem medir não garante menor latência.

A classificação é uma orientação inicial baseada no nome e no valor que o driver publica. Ela não substitui a documentação do fabricante nem prova que uma propriedade está causando um problema. Compare alterações isoladas, com a mesma cena/carga, e observe perda, jitter, throughput, CPU e contadores de erro/descarte.

## Validação pendente

A suíte automatizada verifica o parser, as classificações, a proteção contra CSV formula injection e que o script gerado só usa cmdlets de leitura. A captura real ainda precisa ser executada em Windows com NICs Ethernet e Wi-Fi de diferentes fabricantes, pois este ambiente de desenvolvimento não possui Windows/PowerShell/NetAdapter nem o SDK .NET para executar a aplicação.

## Referências oficiais

- [Get-NetAdapterAdvancedProperty — Microsoft Learn](https://learn.microsoft.com/en-us/powershell/module/netadapter/get-netadapteradvancedproperty)
- [Network Adapter Performance Tuning — Microsoft Learn](https://learn.microsoft.com/en-us/windows-server/networking/technologies/network-subsystem/net-sub-performance-tuning-nics)
- [Interrupt Moderation — Windows drivers](https://learn.microsoft.com/en-us/windows-hardware/drivers/network/interrupt-moderation)
- [Receive Side Scaling — Windows drivers](https://learn.microsoft.com/en-us/windows-hardware/drivers/network/introduction-to-receive-side-scaling)
