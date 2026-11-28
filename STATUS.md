# Status — ynoez 0.3.1

**Situação:** candidato. As alterações desta versão foram feitas sem compilador disponível; compile e rode os testes
(`dotnet test`) e a CI antes de qualquer uso. Não é release estável.

## O que o app tem hoje
- Início com abertura animada do sigilo e login local de demonstração (aceita qualquer texto; não valida, não guarda e não transmite credenciais).
- Menu: Home, Otimizações, Debloat, Foco de Jogo, Placa-mãe.
- **Otimizações:** um botão aplica ajustes de jogo (Game DVR/captura, efeitos visuais, atrasos de menu), MMCSS Games e plano Alto desempenho, com backup e botão de desfazer.
- **Debloat:** AppX opcional por allowlist fechada (sem restauração automática), serviços/tarefas opcionais com backup, limpeza Temp/Brave/Discord, com relatório final.
- **Foco de Jogo:** CS2 e Roblox (todas as instâncias) com prioridade conservadora, XT protegido e restauração ao encerrar.
- **BIOS / Placa-mãe:** detecção de UEFI, Secure Boot, TPM, virtualização, DMA, VBS/HVCI e anti-cheats; escolha de jogos/anti-cheats; gravação apenas via Lenovo WMI ou ASUS ACT em modelos compatíveis; demais placas recebem guia manual.

## Limites
- Nenhum ganho de FPS/ping é prometido sem medição no PC real.
- GIGABYTE A520M K V2: guia manual; o app não grava a BIOS dessa placa nem altera tensão, clock ou limites de potência.
- Sem assinatura Authenticode (sem certificado configurado).
- Detalhes e níveis de validação: `docs/AUDIT-0.3.1.md`.