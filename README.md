# ynoez — perfis de hardware e debloat seletivo

Repositório de dados de compatibilidade, configurações de preservação e perfis de placas-mãe para o **ynoez**.

O objetivo é separar o que é específico de cada hardware, evitar tweaks universais sem comprovação e deixar explícitos os componentes que não podem ser removidos.

## Estrutura

- `Profiles/Motherboards/`: dados do fabricante, revisões, páginas oficiais, recursos e utilitários compatíveis.
- `Configs/Motherboards/`: política de debloat/preservação e limites de segurança específicos da placa.
- Os perfis são dados de referência; não são scripts para executar às cegas.

## Primeiro perfil: GIGABYTE A520M K V2

Inclui revisões 1.0, 1.1 e 1.2. A revisão SMBIOS pode ser genérica; confirme a revisão impressa na própria placa antes de escolher BIOS.

- [Perfil de hardware](Profiles/Motherboards/GIGABYTE/A520M-K-V2.json)
- [Configuração de preservação](Configs/Motherboards/GIGABYTE/A520M-K-V2-safe.json)
- [Página oficial de suporte](https://www.gigabyte.com/br/Motherboard/A520M-K-V2-rev-10/support)

## Regras de segurança

Nunca desativar em lote Microsoft Store/licenciamento, impressão, Windows Search, Windows Update, Defender, SmartScreen, UAC ou drivers de chipset/rede/áudio. Preservar TPM, Secure Boot, VBS/HVCI e IOMMU/DMA, especialmente para compatibilidade com Vanguard e FACEIT.

Não automatizar flash de BIOS, overclock, tensão, PBO, BCD/HPET ou remoção de credenciais. Cada ação que venha a ser implementada deve ter detecção de compatibilidade, backup, confirmação, log e reversão.

## Adicionar outra placa

Só criar um perfil após validar modelo, fabricante, revisões, drivers e ferramentas na documentação oficial. Veja [o guia de perfis](Profiles/Motherboards/README.md).

## Estado

A base de perfis está começando pela A520M K V2. Não é um catálogo completo de placas-mãe ainda.
