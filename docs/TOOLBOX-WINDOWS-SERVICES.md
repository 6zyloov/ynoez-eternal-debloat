# Toolbox — inventário de serviços do Windows

## Escopo

A seção "Inventário ao vivo de serviços" consulta a classe WMI `Win32_Service` em `root\CIMV2`. A coleta é iniciada somente quando o usuário pressiona "Consultar serviços". Ela lê o nome interno, nome de exibição, descrição, estado, tipo de inicialização e PID. A tela permite filtrar localmente, abrir o console oficial `services.msc`, selecionar um serviço para ver sua classificação e exportar o inventário em CSV.

A consulta é somente leitura. Não chama Set-Service, Start-Service, Stop-Service, sc.exe config, não escreve no Registro e não encerra processos. A exportação não contém caminho binário ou conta de logon dos serviços.

## Classificações

- **Protegido pelo ynoez:** componentes que o projeto não deve desativar, como Windows Search, impressão, Defender, Firewall, Windows Update, Microsoft Store/licenciamento, rede, áudio, WMI, TPM, NVIDIA e serviços de anti-cheat conhecidos.
- **Opcional avaliado:** somente os seis serviços que já pertencem à whitelist reversível da página Debloat: DiagTrack, dmwappushservice, MapsBroker, Fax, RetailDemo e WMPNetworkSvc. A classificação não os desativa; a alteração continua sendo uma ação separada com confirmação, backup, verificação e restauração.
- **Somente leitura:** todos os demais serviços. Não há recomendação de desativação automática para itens sem análise individual.

A classificação é conservadora: o estado desconhecido nunca é interpretado como autorização para alterar.

## Segurança

O CSV entre aspas neutraliza fórmulas de planilha que começam com `=`, `+`, `-`, `@`, tabulação ou retorno de carro. O inventário é ordenado por nome de exibição e permite consulta sob demanda. O WMI possui timeout de 20 segundos; a enumeração verifica cancelamento entre entradas.

## Limitações

- A classificação é uma política conservadora do ynoez, não uma análise profunda de dependências de serviços.
- O PID e estado são um snapshot no instante da consulta e podem mudar logo depois.
- Serviços de terceiros desconhecidos ficam como "Somente leitura", não são tratados como inúteis.
- Esta tela não substitui o Gerenciador de Serviços e não é um monitor de mudanças em tempo real.
