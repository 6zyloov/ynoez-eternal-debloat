# Auditoria e mudanças — ynoez 0.3.1

Base: commit `13af033b58dadf361942a167270d43cabb89f669` (0.3.0). Esta rodada foi feita **sem SDK .NET e sem rede**:
o código foi lido, editado e revisado, mas **não foi compilado nem executado**. Veja "Níveis de validação" no final.

## Matriz da auditoria

| Situação | Item |
|---|---|
| Funciona (implementado, com testes existentes) | Allowlist AppX, serviços/tarefas com backup, limpeza Temp/Brave/Discord, perfil de foco (CS2/Roblox/XT), inventários, Lenovo WMI (lógica) |
| Funciona parcialmente | BIOS: só Lenovo WMI e ASUS ACT escrevem, e somente com ferramenta/modelo compatíveis; A520M K V2 recebe apenas guia manual |
| Incompleto | Medição antes/depois na interface (há `BenchmarkSessionService`/`PresentMonService`, sem tela); adaptadores Dell, HP, GIGABYTE, MSI (sem API oficial documentada); assinatura Authenticode; sem entrada de interface para o backup/restore geral |
| **Incorreto — corrigido** | 1) `cpu-boost` entrava como requisito de **todo** anti-cheat, mesmo EAC/BattlEye, e bloqueava a gravação Lenovo quando a BIOS não expunha essa opção. 2) Detecção Lenovo, ASUS e do perfil A520M comparava texto em MAIÚSCULAS com texto já em minúsculas, então **nunca ativava**; o perfil A520M também não casava com "Gigabyte Technology Co., Ltd.". 3) Gravação na BIOS não pedia confirmação explícita. 4) Debloat só mostrava contagens, sem relatório do que foi removido/mantido/ignorado, e não avisava que AppX não tem restauração automática. 5) Confirmações usavam a `MessageBox` clara do Windows. 6) Documentação (`STATUS.md`, release notes) descrevia abas e páginas que não existem no build. |
| Não testado | Tudo em Windows real; esta rodada não compilou nem rodou testes |
| Removido | 39 membros inalcançáveis do `MainWindow` (≈1.700 linhas): páginas Monitor, Backup, Restore, Logs, Rede, Toolbox, cartões AppX/Win32/inicialização/serviços/tarefas e seus auxiliares. `MainWindow.xaml.cs` foi de 2179 para ~490 linhas |

## Requisitos de anti-cheat (fonte oficial FACEIT)

Suporte FACEIT: TPM 2.0 e Secure Boot obrigatórios para todos desde 25/11/2025; IOMMU e VBS são exigidos em ondas.
O mapa continua pedindo Secure Boot, TPM, IOMMU e virtualização para FACEIT, mas **Core Performance Boost / Turbo Boost
não é requisito de nenhum anti-cheat**: agora é uma opção de desempenho separada, desmarcada por padrão.
EAC e BattlEye continuam sem requisito de firmware inventado.

## Serviços que ficaram sem chamador na interface

A remoção das páginas mortas deixou estes serviços sem uso direto no app (continuam cobertos por testes): `BackupService`,
`AutoProfileService`, `BenchmarkSessionService`, `PresentMonService`, `NetworkMonitorService`, `NetworkDiagnosticService`,
`NetworkAdapter*`, `AdapterPowerManagementService`, `Win32ProgramRemovalService`, `WindowsRepairService`,
`WindowsIntegrityAuditService`, `QuickGameOptimizationService` e inventários. Não foram apagados porque os testes e o
relatório do projeto os tratam como mecanismos internos. Decisão pendente: religar o que for útil (ex.: diagnóstico de rede
dentro de OTIMIZAR) ou remover de vez.

## Níveis de validação (0.3.1)

| Mudança | 1 Implementado | 2 Compila | 3 Testes automáticos | 4 VM Windows | 5 Hardware físico |
|---|---|---|---|---|---|
| cpu-boost separado de anti-cheat | sim | não verificado | escritos, **não executados** | não | não |
| Correção maiúsc./minúsc. Lenovo/ASUS/A520M | sim | não verificado | escritos, **não executados** | não | não |
| Confirmação antes de gravar BIOS | sim | não verificado | — | não | não |
| Relatório do Debloat | sim | não verificado | — | não | não |
| Diálogo escuro (`ThemedDialog`) | sim | não verificado | — | não | não |
| Desfazer otimização | sim | não verificado | — | não | não |
| Abertura animada do sigilo | sim | não verificado | — | não | não (apenas simulação dos quadros fora do WPF) |
| Remoção de código morto | sim | não verificado | — | não | não |

"Não verificado" = nenhum compilador foi executado. A verificação feita foi léxica e estrutural (balanceamento de chaves,
parênteses e colchetes; XAML bem-formado; nenhuma referência restante aos membros removidos).
