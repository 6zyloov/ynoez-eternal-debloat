# Pesquisa técnica — ecossistema de debloat e otimização Windows

Data da revisão: 27/11/2026. Este documento registra inspeção de repositórios públicos, documentação e arquivos-fonte específicos. Não significa que todos os tweaks foram executados ou que seus efeitos foram medidos no mesmo hardware.

## Princípio

O ynoez não será um agregador de scripts cegos. Cada função será reimplementada como uma operação tipada, com pré-condições, detecção de estado, backup, aplicação, verificação pós-ação e reversão. Nenhuma contagem de tweaks ou promessa de FPS será usada como critério de qualidade.

## Projetos examinados

| Projeto | Material inspecionado | O que aproveitar como referência | O que não copiar cegamente |
|---|---|---|---|
| Chris Titus Tech WinUtil | README e `config/tweaks.json` (commit/blob `471cc4c3e4bcc133310ad5b42b5565111186dae9`) | Catálogo declarativo de tweaks, instalação de apps, correções e gestão de updates; campos para descrição, categoria, registro e valor original | Presets que alterem políticas sem confirmar edição/build; scripts de terceiros sem validar estado atual |
| Raphire Win11Debloat | `Win11Debloat.ps1`, `Config/Apps.json`, `Config/Features.json` | Catálogo separado de apps e funcionalidades; recomendações; suporte a backup, restore point, seleção e execução configurável | Remoção de apps/recursos marcada como segura sem avaliar dependências específicas e preferências deste PC |
| Zenith | `Zenith.ps1`, README e testes listados no repositório | Detecção por hardware, health check, sessões de jogo, métricas FPS/1% lows, rede e reversão | Prioridades/afinidades e tweaks globais aplicados sem medir impacto ou testar anti-cheat |
| hellzerg Optimizer | `MainForm.CleanerLogic.cs`, `MainForm.Dns.cs` | UI segmentada, limpeza, diagnóstico de DNS e ferramentas de sistema | Código GPL-3.0 não será incorporado ao repositório MIT sem decisão explícita de relicenciamento/isolamento |
| optimizerDuck | README, árvore de testes e licença | Arquitetura .NET, testes xUnit, estados de otimização e serialização de operações de reversão | Código GPL-3.0 não será copiado para o núcleo MIT; conceitos serão reimplementados |
| Winhance | README, árvore de código e catálogo Apply/Detection/Revert | Separação entre catálogo, detecção, plano de aplicação, executor e recuperação; testes | Não copiar código sem revisão de licença e dependências |
| AtlasOS | arquitetura e documentação de performance | Disciplina de documentação, medição e validação de alterações no sistema | Não adotar imagem Windows modificada ou remover componentes essenciais do sistema do usuário |
| Sophia Script | README e estrutura PowerShell | Ajustes granulares, documentação de versões e políticas do Windows | Scripts em massa sem uma camada de detecção/reversão do ynoez |

## Evidências de implementação observadas

- WinUtil mantém um catálogo JSON com nome, descrição, categoria e operações de registro, incluindo valor original. É uma boa base conceitual para um catálogo de mudanças declarativo.
- Win11Debloat separa apps de funcionalidades do Windows e inclui mecanismos de configuração, backup e ponto de restauração no script principal.
- Zenith documenta adaptações por tipo de PC, execução em segundo plano, backup de valores originais, detecção de hardware e medição de FPS médio/1%/0,1% lows.
- OptimizerDuck tem uma suíte de testes extensa para operações de reversão, modelos de serviços, tarefas agendadas, energia e estado da otimização.
- Winhance separa detecção, plano, executor e recuperação. Essa separação é uma referência importante para não manter toda a lógica em code-behind.

## Licenças — regra obrigatória

- MIT: WinUtil, Win11Debloat, Zenith e Sophia Script.
- GPL-3.0: hellzerg Optimizer, optimizerDuck e AtlasOS.
- A licença do Winhance deve ser confirmada no arquivo de licença vigente antes de qualquer reutilização.
- O repositório ynoez permanece MIT por enquanto. Código MIT só pode ser incorporado com os avisos de copyright/licença exigidos. Código GPL não será copiado para o núcleo MIT sem uma decisão de relicenciamento compatível. Reimplementar uma ideia não autoriza copiar trechos expressivos, assets ou textos.
- Nenhum executável fechado de tweak será distribuído só porque aparece em um pacote de terceiros. Binários externos precisam de origem, hash, assinatura, licença e justificativa verificáveis.

## Referências primárias

- https://github.com/ChrisTitusTech/winutil
- https://github.com/Raphire/Win11Debloat
- https://github.com/daltonlyon/Zenith
- https://github.com/hellzerg/optimizer
- https://github.com/itsfatduck/optimizerDuck
- https://github.com/memstechtips/Winhance
- https://github.com/Atlas-OS/Atlas
- https://github.com/farag2/Sophia-Script-for-Windows

## Conclusão

As melhores ideias para o ynoez não são “mais tweaks”: catálogo auditável, detecção real de estado, compatibilidade por build/hardware, backup granular, undo testado, medição antes/depois, operação em background e explicação clara do impacto. Essas capacidades serão o padrão de aceite de cada módulo.
