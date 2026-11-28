# Status — ynoez 0.3.0

**Situação:** candidato integrado na PR draft #12. A revisão 94a4549e67a5131f6f377e1e74cf1b0519e5fdae passou nas 24 etapas da CI Windows: auditoria NuGet, 259 testes, compilação, instalador, SBOM, instalação/reinstalação/desinstalação, upgrade, rollback, re-upgrade, retenção de dados, checksums e publicação de artefatos. Ainda não é release estável.

CI aprovada: https://github.com/6zyloov/ynoez-eternal-debloat/actions/runs/46108559354

## Interface

- Tema preto absoluto/branco, sem separadores superiores claros e sem scrollbar visível; rolagem por mouse/teclado continua.
- Login local de demonstração aceita qualquer texto; não valida, não armazena e não transmite credenciais.
- Home compacta com CPU/GPU/RAM e atalhos.
- Menu lateral: Home, Otimizações, Debloat, Rede, Foco de Jogo, Placa-mãe, Toolbox e Backup. Monitor, Restore e Logs não aparecem na navegação.

## Otimizações

- Main: Game Mode, Game DVR, efeitos visuais, animações, transparência e atraso de menus/hover.
- Power: Alto desempenho/Equilibrado, perfis automáticos e restauração do plano original.
- Kernel: MMCSS Games, NetworkThrottlingIndex, PowerThrottlingOff, Win32PrioritySeparation Programs 0x26 e timer 1 ms apenas no processo ynoez.
- GPU: HAGS reversível, preferência por aplicativo no Foco de Jogo e atalhos AMD/Windows.
- Rede: RSS, Interrupt Moderation e EEE somente quando o driver informa opções compatíveis; economia de energia por adaptador com backup.
- Foco de Jogo: detecção de CS2 e Roblox; uma regra cobre todas as instâncias RobloxPlayerBeta.exe; CPU/memória e preferência de adaptador gráfico com journal de restauração.

## Debloat

- Limpeza Temp/Brave/Discord.
- AppX opcional com allowlist fechada, remoção somente da conta atual e sem rollback automático.
- Win32 com desinstalador direto: MSI via ProductCode ou executável registrado. Bloqueia command hosts e componentes conhecidos de Windows/driver/runtime/anti-cheat.
- Inicialização do usuário com backup/reversão.
- Serviços e tarefas opcionais com whitelist, confirmação, UAC, backup e verificação.
- Leituras de inicialização, serviços e tarefas em segundo plano.

## Placa-mãe

- Perfil GIGABYTE A520M K V2 com recomendações XMP/DOCP, Core Performance Boost, PBO, Smart Fan 5 e SAM/Re-Size BAR.
- UEFI por reinicialização explícita; links BIOS dependem da revisão física confirmada no PCB.
- Não grava variáveis de BIOS/UEFI, não faz flash nem altera tensão.

## Limites

- Win32PrioritySeparation 0x26 é política de quantum/foreground, não multiplicador de CPU.
- NetworkThrottlingIndex é legado; HAGS/MMCSS/RSS/EEE/Interrupt Moderation são dependentes de Windows/driver e precisam de A/B.
- PowerThrottlingOff pode aumentar consumo/temperatura.
- AppX não tem rollback automático; Win32 usa o desinstalador oficial do fabricante.
- Nenhum ganho de FPS/ping é prometido sem validação no PC real.
- Authenticode NOT SIGNED sem certificado Code Signing.

## Artefatos

Artefatos publicados na execução CI acima: ynoez-portable-win-x64 e ynoez-setup-win-x64. SHA256SUMS.txt acompanha os arquivos.