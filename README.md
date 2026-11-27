# ynoez-eternal-debloat

Base de perfis, diagnósticos e configurações reversíveis do **ynoez** para Windows. O projeto reúne otimizações de sistema, rede, jogos e hardware sem transformar o Windows em uma instalação frágil.

> **Estado atual:** estrutura inicial em evolução. O primeiro perfil é a **GIGABYTE A520M K V2**, com diferenças registradas por revisão 1.0/1.1/1.2.

## Princípios
- Diagnosticar antes de alterar; mostrar prévia (dry-run) e pedir consentimento.
- Backup e restauração para ações mutáveis; logs apenas locais.
- Preservar Riot Vanguard, FACEIT Anti-Cheat e proteções do Windows.
- Não desativar Defender, Windows Update, TPM, Secure Boot, VBS/HVCI ou IOMMU.
- Não remover Store, Impressão, Calculadora, Clipboard, NVIDIA Instant Replay/Alt+Z, Windows Search ou Bloco de Notas.
- Não remover dados pessoais ou perfis de navegador sem consentimento.
- Nenhuma promessa de velocidade acima do plano ISP ou de corrigir falhas de roteador/provedor.

## Estrutura
- `profiles/motherboards/`: perfis específicos por modelo e revisão.
- `profiles/vendors/software-ecosystems.json`: mapa dos ecossistemas oficiais de utilitários por fabricante.
- `profiles/catalog.json`: índice dos perfis publicados.
- `docs/`: critérios de segurança, fontes e processo de expansão.
- `scripts/diagnostics/`: diagnósticos somente de leitura.

## Central da placa-mãe
O perfil A520M K V2 separa Smart Fan 5 (revisões 1.0/1.1) de Smart Fan 6 (revisão 1.2), registra drivers oficiais e exige confirmação da revisão antes de orientar BIOS. O diagnóstico PowerShell inicial apenas lê informações SMBIOS, BIOS, CPU, memória, dispositivos de rede, drivers e estado de recursos de segurança.

## Ecossistemas pesquisados
A estrutura inicial cobre GIGABYTE APP Center, ASUS Armoury Crate, MSI Center e ASRock APP Shop. Isso não significa que todos os utilitários funcionem em todas as placas: cada perfil precisa validar o suporte na página oficial do modelo/revisão.

## Documentação
- [Modelo de segurança e restauração](docs/SAFETY-MODEL.md)
- [Perfis de placas-mãe e ecossistemas dos fabricantes](docs/MOTHERBOARD-PROFILES.md)

## Fontes oficiais
- [GIGABYTE A520M K V2 — revisões e suporte](https://www.gigabyte.com/br/Motherboard/A520M-K-V2-rev-10/Support)
- [AMD A520 — drivers de chipset](https://www.amd.com/pt/support/downloads/drivers.html/chipsets/am4/a520.html)
- [ASUS Armoury Crate](https://www.asus.com/support/faq/1041776/)
- [MSI Center](https://www.msi.com/support/technical_details/MB_SW_MSI_Center)
- [ASRock APP Shop](https://www.asrock.com/feature/appshop/)

## Estado de validação
Os JSONs são validados durante a publicação. O script PowerShell é de leitura, mas ainda precisa de teste em Windows real com diferentes versões SMBIOS e hardwares; nenhum resultado de execução em seu computador foi presumido.
