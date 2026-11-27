# ynoez-eternal-debloat

Base de perfis, diagnósticos e configurações reversíveis do **ynoez** para Windows. O projeto reúne otimizações de sistema, rede, jogos e hardware sem transformar o Windows em uma instalação frágil.

> **Estado atual:** estrutura inicial; primeiro perfil documentado: **GIGABYTE A520M K V2**. Os perfis são dados e recomendações. Nenhum ajuste é aplicado automaticamente por este repositório.

## Princípios
- Seguro por padrão: diagnóstico antes de sugerir alterações.
- Reversibilidade: backup antes de alterar registro, serviços, energia ou rede.
- Consentimento: prévia e confirmação explícita antes de executar.
- Compatibilidade com Riot Vanguard e FACEIT Anti-Cheat.
- Preservar Defender, Windows Update, TPM, Secure Boot, VBS/HVCI e IOMMU/DMA remapping.
- Não remover Microsoft Store, Impressão, Calculadora, Clipboard, NVIDIA Instant Replay/Alt+Z, Windows Search ou Bloco de Notas.
- Diagnósticos locais, sem telemetria própria.
- Não prometer velocidade acima do plano ISP ou corrigir falhas externas de roteador/provedor.

## Estrutura
- `profiles/motherboards/`: perfis por fabricante, modelo e revisão.
- `profiles/catalog.json`: índice de perfis.
- `docs/`: critérios de segurança e manutenção.
- `scripts/diagnostics/`: diagnósticos somente de leitura.
- `schemas/`: formatos de dados para a interface.

## A520M K V2
O perfil contempla revisões 1.0, 1.1 e 1.2 separadamente. A revisão precisa ser detectada ou confirmada antes de recomendar BIOS, firmware ou pacotes específicos. Inclui referências oficiais a chipset AMD, LAN Realtek, áudio, APP Center, Q-Flash, Smart Fan 5 e utilitários documentados.

**Não há overclock automático, mudança de tensão nem flash de BIOS automático.**

## Diagnóstico
`scripts/diagnostics/Get-YnoezBoardReport.ps1` consulta informações do Windows e imprime JSON. É somente leitura e não exige elevação administrativa. Para salvar, informe explicitamente `-OutputPath`.

## Segurança
Consulte [docs/SAFETY-MODEL.md](docs/SAFETY-MODEL.md) e [docs/MOTHERBOARD-PROFILES.md](docs/MOTHERBOARD-PROFILES.md).

## Fontes oficiais
- [GIGABYTE A520M K V2 — suporte](https://www.gigabyte.com/br/Motherboard/A520M-K-V2-rev-10/Support)
- [GIGABYTE A520M K V2 — especificações](https://www.gigabyte.com/br/Motherboard/A520M-K-V2-rev-10/sp)
- [AMD — drivers do chipset A520](https://www.amd.com/pt/support/downloads/drivers.html/chipsets/am4/a520.html)
- [Manual GIGABYTE A520M K / K V2](https://download.gigabyte.com/FileList/Manual/mb_manual_a520m-k-series_e.pdf)

As páginas podem mudar. O ynoez deve consultar as fontes oficiais no momento da verificação; nunca tratar uma versão antiga como “a mais recente”.
