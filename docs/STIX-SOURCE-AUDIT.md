# Auditoria independente — Stix Free Tweaking Utility

Data: 27/11/2026. Inspeção estática do repositório público StixPcServices1/Stix-Free-Tweaking-Utility, incluindo TweaksControl.cs, DebloatControl.cs, NetworkControl.cs, BackupControl.cs, HomePage.cs, Forms/Form1.cs e changelog. Nenhum executável foi executado e nenhum tweak foi aplicado. O repositório consultado não contém arquivo de licença explícito; por isso não copiamos código, recursos visuais, textos ou binários.

## Arquitetura e interface

- Aplicativo WinForms com controles Guna UI2/Cuore UI.
- Navegação por Home, Backup, Tweaks, Network e Debloat.
- Home consulta CPU/GPU/placa-mãe/Windows por WMI e taxa de rede.
- Backup solicita ponto de restauração do Windows.
- Tweaks agrupa registro, energia, CPU/GPU, áudio, memória e sistema.
- Debloat agrupa serviços, AppX, limpeza e ferramentas auxiliares.
- Network aplica propriedades avançadas de adaptadores.

A organização por áreas é útil como referência. O ynoez mantém implementação própria WPF e uma camada de serviços/catálogo; não reutiliza bibliotecas/artefatos sem licença confirmada.

## Riscos concretos encontrados

- Os handlers chamam comandos elevados individualmente via cmd/PowerShell. Não foi encontrado um journal transacional por ação que preserve valores anteriores e faça rollback individual. O ponto de restauração do Windows não substitui backup por operação.
- Um handler baixa um arquivo BAT de um link Dropbox e o executa como administrador sem hash fixado ou assinatura verificada. O ynoez não repetirá esse padrão: dependências externas precisam de origem oficial, versão e hash.


| Área | Operações identificadas no código | Decisão ynoez |
|---|---|---|
| Segurança do Windows | Desativar UAC, vulnerable driver blocklist, VBS, HVCI e DMA remapping | Bloqueado. Preservar UAC, TPM, Secure Boot, VBS/HVCI e IOMMU/DMA |
| Defender e Updates | Desativar Sense, SecurityHealthService, UsoSvc, DoSvc e políticas de Windows Update | Bloqueado. Defender e Windows Update permanecem protegidos |
| Serviços essenciais | Desativar WSearch e Spooler, além de outros serviços sem checagem de dependências | Bloqueado. Search e impressão permanecem protegidos |
| TPM e Intel | Desativar Intel TPM Provisioning Service e vários serviços de chipset/gerenciamento | Bloqueado. Não inferir que serviço Intel é inútil pelo nome |
| Rede | Aplicar grande mapa de RegistryKeyword a todos os adaptadores: RSS, EEE, Flow Control, Wake-on-LAN, interrupt moderation, jumbo frame, buffers, selective suspend, power saving e recursos específicos de vendors | Não copiar. Cada NIC/driver tem propriedades diferentes; ynoez só altera opções suportadas e selecionadas, com backup e verificação |
| CPU/kernel | Win32PrioritySeparation, MinimumDpcRate, DpcWatchdogPeriod/DpcTimeout, WorkerFactory timeouts, Threaded DPC e outras chaves internas | Bloqueado sem evidência reproduzível, documentação oficial e teste específico de hardware/build |
| Memória/armazenamento | Desativar Memory Compression, alterar SvcHostSplitThreshold, fsutil encryption/8dot3/last access/resource manager e limpar Prefetch | Não aplicar em lote. Pode degradar compressão, compatibilidade, armazenamento ou cache |
| GPU | Escritas em chaves internas NVIDIA e AMD como RM* power gating, EnableUlps, UVD/VCE clock gating, VSyncControl e HAGS | Sem alteração universal. Separar por vendor/modelo/driver e medir antes/depois |
| Debloat | Remover AppX para todos os usuários, desinstalar OneDrive, remover pastas ASUS/Armoury Crate e desativar serviços OEM | Não executar em massa. Identificar publisher, dependências, hardware e possibilidade real de reinstalação |
| Energia | Desativar hibernação, Modern Standby, SleepStudy e recursos de economia sem distinguir desktop/laptop | Perfil condicionado ao hardware; preservar modo de energia original e reversão |
| Dependências externas | Baixar script BAT de Dropbox e executar elevado; baixar Autoruns de Sysinternals | Não executar scripts externos sem hash, origem verificável e revisão; ferramentas oficiais podem ser abertas sob demanda |
| Backup/rollback | Criar ponto de restauração do Windows, mas comandos individuais não guardam estado original por ajuste | Ponto de restauração opcional + backup granular e undo verificado por ação |

## Evidência de manutenção

O changelog público registra que versões posteriores removeram ThreadDpcEnable, ajuste de prioridade de CSRSS e desativação de MMCSS. Isso reforça a política de não incluir tweaks de scheduler/DPC apenas por popularidade.

## O que foi incorporado como ideia, sem copiar código

- Áreas separadas para inventário, backup, tweaks, rede e debloat.
- Restore point opcional e backup local granular.
- Catálogo de ações com detecção e reversão.
- Diagnóstico de rede e ferramentas específicas por NIC.
- UI escura com sidebar e destaque rosa como referência visual geral, com implementação própria.

## Fontes

- https://github.com/StixPcServices1/Stix-Free-Tweaking-Utility
- https://github.com/StixPcServices1/Stix-Free-Tweaking-Utility/blob/main/Changelog.cd
- https://stixtweaks.com/pages/stix-free-tweaking-utility
- https://stixtweaks.com/free-tweaks
