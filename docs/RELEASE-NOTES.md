# ynoez 0.3.0

Primeira versão de referência do ynoez para Windows x64.

## Componentes principais
- Painel local de hardware, monitoramento de CPU/RAM/GPU e benchmark PresentMon.
- Sessões A/B de benchmark com três rodadas por fase, mediana e dispersão.
- Diagnóstico de rede, monitoramento contínuo, inventário de adaptadores e relatório CSV.
- Ajustes reversíveis com backup, verificação e restauração.
- Inventários Win32 e AppX/MSIX, incluindo pacotes provisionados em modo somente leitura.
- Perfis Performance, Equilibrado e AFK com proteção de processos do usuário.
- Instalador em C:\ynoez e pacote portátil.

## Integridade
Os arquivos de instalação e portabilidade são acompanhados por SHA256SUMS.txt. Compare os hashes antes de executar os binários. A página de release contém os hashes oficiais publicados pelo próprio repositório.

## Limites conhecidos
A release é compilada e testada em runner Windows x64 do GitHub. A compatibilidade física com Vanguard/FACEIT e o comportamento em cada hardware ainda precisam ser validados no PC de destino. Nenhum ganho de FPS ou estabilidade de rede é prometido.
