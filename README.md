# ynoez — Performance Tuner para Windows

Aplicativo nativo em C# / WPF / .NET 10, Windows x64. Interface minimalista em preto absoluto e branco, com cartões compactos e interruptores.

## Painel

- Home: CPU, GPU, RAM e atalhos rápidos. Sem gráficos ou inventários na página inicial.
- Otimizações: abas Main, Power, Kernel e GPU.
- Debloat: Apps, Win32, Inicialização, Serviços e Tarefas.
- Rede: DNS, economia de energia e propriedades avançadas disponíveis no driver.
- Foco de Jogo: prioridade do CS2 e das instâncias Roblox AFK.
- Placa-mãe: recomendações da placa reconhecida e acesso à UEFI.
- Toolbox: atalhos diretos para as ferramentas do Windows e do driver.
- Backup: snapshot local e ponto de restauração.

## Otimizações

### Main

Game Mode, Game DVR/captura em segundo plano, efeitos visuais, animações, transparência, atraso de menus e hover do mouse. Os valores de Registro por usuário têm backup e restauração individual.

### Power

Planos Alto desempenho e Equilibrado, perfis automáticos baseados em jogo/inatividade e recuperação do plano anterior. Os perfis protegem jogos e runtimes comuns de automação; não encerram processos.

### Kernel

- MMCSS Games: prioridade e categoria apenas na tarefa Games.
- NetworkThrottlingIndex: ajuste multimídia legado; exige comparação A/B.
- Power Throttling: desativa a política global de economia de CPU; pode aumentar consumo e temperatura.
- Win32 Priority Separation: perfil Programs 0x26. É política do escalonador, não multiplicador ou clock de CPU.
- Temporizador 1 ms: solicitação apenas ao processo ynoez, pareada com timeEndPeriod ao sair. Não altera HPET ou BCD.

### GPU

HAGS com backup, preferência gráfica por aplicativo no Foco de Jogo, AMD Adrenalin e Configurações gráficas do Windows. HAGS depende de GPU/driver/Windows e exige reinicialização.

## Rede

Para cada NIC, o painel usa os valores válidos anunciados pelo driver. Pode habilitar RSS e testar Interrupt Moderation/EEE desligados quando essas propriedades existem. Também controla economia de energia do adaptador e limpa o cache DNS. Cada mudança guarda o valor original e evita sobrescrever uma alteração externa posterior.

Interrupt Moderation e EEE podem aumentar consumo de CPU/energia; teste ping e frametime A/B. NetworkThrottlingIndex é legado e pode não produzir efeito em Windows recente.

## Debloat

- Temporários do usuário e caches Brave/Discord.
- AppX: lista fechada de aplicativos opcionais; remove somente na conta atual. Frameworks, recursos, bundles, pacotes não removíveis, componentes essenciais e pacotes provisionados ficam fora. A remoção AppX não tem rollback automático.
- Win32: identifica desinstaladores diretos válidos. MSI usa ProductCode; outros usam o executável registrado e existente. Interpretadores/command hosts, componentes Windows, drivers, runtimes e anti-cheats são bloqueados. O próprio desinstalador do fabricante conduz a remoção.
- Inicialização do usuário: desativação com backup e restauração.
- Serviços opcionais e tarefas de telemetria/feedback/mapas/Xbox: listas allowlist, confirmação, UAC, backup e verificação. Serviços Xbox podem afetar autenticação, multiplayer e saves Xbox/Game Pass.

Consultas de inicialização, serviços e tarefas executam fora da thread da interface para a navegação continuar responsiva.

## Foco de Jogo

Detecção automática do CS2 em execução/bibliotecas Steam e do Roblox mais recente. Uma entrada RobloxPlayerBeta.exe abrange as duas instâncias e futuras instâncias. CS2 usa CPU Alta (com confirmação) ou Acima do normal; Roblox AFK usa Idle ou Abaixo do normal. Prioridade de memória não reserva RAM; preferência gráfica escolhe adaptador, não reserva VRAM.

## Placa-mãe e BIOS

Para GIGABYTE A520M K V2, o painel apresenta recomendações de XMP/DOCP, Core Performance Boost, PBO, Smart Fan 5 e Smart Access Memory/Re-Size BAR, além de abrir a UEFI e links oficiais por revisão física. O ynoez não grava variáveis UEFI, não faz flash de BIOS e não aplica overclock de tensão. Confirme a revisão impressa no PCB antes de usar links de BIOS.

## Segurança e limites

- Registro HKCU/HKLM apenas por allowlist explícita, backup antes da alteração e verificação posterior.
- Não desativa Defender, Firewall, Windows Update, TPM, Secure Boot, VBS/HVCI, Vanguard ou FACEIT.
- Não usa REALTIME_PRIORITY_CLASS, HPET/BCD hacks, alteração de tensão, limites térmicos, afinidade, prioridades de thread ou working set.
- Win32PrioritySeparation 0x26 não promete FPS; o perfil padrão do Windows cliente pode ser equivalente.
- HAGS, MMCSS, RSS, EEE e Interrupt Moderation dependem do sistema/driver e exigem comparação A/B. Nenhum ganho de FPS ou ping é prometido.
- Authenticode fica NOT SIGNED se nenhum certificado Code Signing estiver configurado.

## Build e instalação

- Projeto: src/ynoez/
- Publicação Windows x64: build.bat ou scripts/publish.ps1
- Instalação local: scripts/install.ps1
- Pasta prevista: C:\ynoez\
- CI: .github/workflows/build.yml

A CI roda em Windows Server 2025 e não substitui testes visuais e funcionais em Windows cliente/hardware real, incluindo a sessão CS2 + duas instâncias Roblox + XT.