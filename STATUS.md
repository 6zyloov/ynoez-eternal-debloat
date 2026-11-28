# Status do projeto — ynoez 0.3.0

**Situação:** candidato integrado na PR draft #12. O commit anterior (5182d562dce54b5c14456535a448035933c59545) passou pela CI Windows Server 2025; esta rodada acrescentou SBOM SPDX, controles opcionais de Authenticode e teste de upgrade/rollback entre main e candidato. A validação automatizada da nova rodada ainda está pendente da execução do GitHub Actions. Não há release estável publicada.

## Funcionalidades integradas

- **Home e hardware:** CPU, GPU, memória, Windows, placa-mãe e BIOS via APIs/WMI. Inventário de DIMMs com dados SMBIOS/WMI, sem afirmar leitura SPD ou topologia de canal.
- **Monitor:** CPU/RAM por APIs nativas; uso 3D por NVIDIA SMI ou contadores Windows GPU Engine. A agregação WMI soma instâncias de processos que compartilham o mesmo engine físico e escolhe o engine 3D mais ocupado, com testes para múltiplos processos e adaptadores. Temperatura, VRAM e potência dependem do suporte do driver NVIDIA; sensores de CPU e fontes AMD/Intel ainda não estão integrados.
- **Benchmark:** PresentMon oficial baixado sob demanda, com SHA-256 fixado, captura ETW, CSV/JSON, FPS médio, 1%/0,1% low e frametimes. A captura não promete ganhos nem controla a cena do jogo.
- **Perfis e energia:** perfis manuais Performance/Equilibrado/AFK e automação por jogo/inatividade. Jogo ativo com entrada recente usa Performance; jogo ou processo protegido em inatividade usa Equilibrado + bloqueio de suspensão; economia AFK só entra sem cargas protegidas após 15 minutos. Roblox, jogos conhecidos e runtimes comuns de automação ficam protegidos por padrão. A lista personalizada é somada e não substitui os padrões. Configuração ausente/corrompida ou catálogo embarcado inválido mantém AFK protegido e exibe aviso. A tela inclui inventário somente leitura de processos para cadastrar o nome exato do processo XT; nenhum processo é encerrado.
- **Registro e Debloat:** 11 ajustes HKCU transacionais e reversíveis. Limpeza seletiva Temp/Brave/Discord preservando perfis e credenciais. Inventários Win32/AppX/MSIX e provisionados são somente leitura. Inicialização HKCU e Startup pessoal têm backup/reversão; itens de máquina e Startup compartilhada são somente leitura.
- **Serviços e tarefas:** whitelist conservadora de seis serviços e seis tarefas opcionais para alterações reversíveis. Toolbox oferece inventário somente leitura de serviços e tarefas agendadas, filtro, classificação, detalhes e CSV.
- **Rede:** inventário NIC/driver/propriedades avançadas em modo somente leitura; diagnóstico e monitor IPv4/IPv6 ICMP/TCP/DNS/tracert, perda/jitter em janela móvel de 60 segundos e CSV. Energia de NIC só altera opções compatíveis, reversivelmente e sem reiniciar o adaptador.
- **Placa-mãe:** perfil GIGABYTE A520M K V2, inventário PnP e drivers. SMBIOS é apenas indício; BIOS por revisão só aparece após confirmação manual da serigrafia do PCB.
- **Manutenção e recuperação:** DISM/SFC em comandos allowlisted, snapshots locais com SHA-256, backups e restauração de Registro/energia/serviços/NIC/tarefas/inicialização. A bandeja mantém operações ativas quando a janela é fechada.
- **Instalador e distribuição:** .NET 10 self-contained win-x64; versão única no csproj; Inno Setup para C:\ynoez; metadados PE e SHA-256. O pipeline de CI foi ampliado com SBOM SPDX 2.2, status explícito de assinatura, assinatura Authenticode opcional e teste de instalação/reinstalação/desinstalação. Também executa upgrade do baseline main para o candidato, rollback para o baseline e re-upgrade, conferindo os hashes dos executáveis e a retenção das pastas de dados.

## Regras de segurança

Permanecem protegidos Microsoft Store/licenciamento, impressão, Calculadora, Clipboard, Notepad, Windows Search, Windows Update, Defender, Firewall, SmartScreen, UAC, NVIDIA Instant Replay/Alt+Z, Vanguard, FACEIT, TPM, Secure Boot, VBS/HVCI e IOMMU/DMA. Não há flash de BIOS, overclock, tensão, alterações BCD/HPET/PlatformTick ou desativação em massa de serviços. AppX não é removido.

## Limites que ainda impedem chamar de release estável

1. A nova CI precisa terminar com sucesso para validar a rodada de SBOM, assinatura e migração/rollback.
2. Testar em Windows cliente: abertura, navegação, WMI, sessões longas, PresentMon em jogos reais, rede e recuperação após falhas.
3. Validar Roblox + automações XT por várias horas, observando que processos e scripts permanecem ativos; cadastrar o nome exato de processos XT independentes dos runtimes protegidos.
4. Testar Vanguard/FACEIT em ambiente autorizado e hardware AMD/Intel/NVIDIA reais. A CI não confirma compatibilidade de anti-cheat.
5. Validar versões suportadas de Windows 11 e Windows 10 Enterprise/LTSC listadas pela Microsoft, contas padrão/administrador e cenários sem Internet. Windows 10 Home/Pro não faz parte da matriz suportada do .NET 10.
6. A CI agora testa upgrade/rollback entre a branch main atual e o candidato; ainda não existe uma versão estável publicada para testar migração de uma release anterior.
7. Assinatura Authenticode real depende de certificado Code Signing válido e secrets configurados. Sem eles, a distribuição declara NOT SIGNED e depende dos hashes SHA-256.
8. Sensores AMD/Intel, MVVM/DI, journal global e recuperação abrangente após crash continuam como débitos de engenharia.

## Política

CI verde é condição necessária, não suficiente. A versão estável só será marcada após testes Windows/hardware real documentados, a instalação/migração/rollback/desinstalação validados e a revisão final dos artefatos. AppX, firmware e alterações de segurança continuam conservadores por desenho.
