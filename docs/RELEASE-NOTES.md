# ynoez 0.3.0 — candidato de Performance Tuner

## Interface

- Home compacta com CPU/GPU/RAM e atalhos diretos.
- Tema preto absoluto/branco, sem divisor superior claro ou scrollbar visível; rolagem por mouse/teclado continua.
- Login de demonstração aceita qualquer entrada sem validar ou guardar credenciais.
- Navegação sem Monitor, Restore ou Logs.

## Otimizações

- Abas Main, Power, Kernel e GPU com interruptores e ações diretas.
- Main: Game Mode, Game DVR/captura, efeitos visuais, animações, transparência e atrasos de menu/hover.
- Power: Alto desempenho/Equilibrado e perfis automáticos por jogo/inatividade.
- Kernel: MMCSS Games, NetworkThrottlingIndex, Power Throttling, Win32PrioritySeparation Programs 0x26 e timer 1 ms por processo.
- GPU: HAGS com backup, preferência gráfica por aplicativo e atalhos AMD/Windows.
- Rede: RSS, Interrupt Moderation e EEE apenas conforme opções válidas do driver; backup e restauração das propriedades.

## Debloat

- Limpeza Temp e caches Brave/Discord.
- Remoção AppX opcional para o usuário atual com allowlist fechada; sem rollback automático.
- Desinstalação Win32 direta por ProductCode MSI ou executável registrado, bloqueando scripts/command hosts.
- Inicialização, serviços e tarefas opcionais com backup e restauração.
- Consultas em background para evitar travamento da navegação.

## Placa-mãe

- Perfil GIGABYTE A520M K V2: XMP/DOCP, Core Performance Boost, PBO, Smart Fan 5 e SAM/Re-Size BAR como recomendações de UEFI.
- Acesso à UEFI e links oficiais por revisão física. O aplicativo não grava BIOS/UEFI, não faz flash e não altera tensão.

## Validação

A revisão 94a4549e67a5131f6f377e1e74cf1b0519e5fdae passou nas 24 etapas da CI Windows: 259 testes, compilação, instalador, SBOM, instalação/reinstalação/desinstalação, upgrade/rollback/re-upgrade, retenção e checksums.

CI: https://github.com/6zyloov/ynoez-eternal-debloat/actions/runs/46108559354

Esta CI não substitui validação visual em Windows cliente nem a sessão real CS2 + duas instâncias Roblox + XT. Win32PrioritySeparation 0x26 não é multiplicador de CPU; NetworkThrottlingIndex é legado; ajustes HAGS/MMCSS/RSS/EEE/Interrupt Moderation exigem comparação A/B. AppX não tem rollback automático. Authenticode depende de certificado Code Signing.