# Status do projeto — ynoez 0.3.0

**Situação:** candidato integrado em PR draft #12. O commit `9fa0e074ae995f346dd93140e96db8f3e0dd2f6d` passou pela CI Windows Server 2025 (run `46088094299`): auditoria NuGet, testes xUnit, publish autocontido, Inno Setup, instalação limpa, reinstalação, desinstalação com preservação de dados, checksums e artefatos. A CI valida compilação e ciclo de vida básico do pacote, não compatibilidade física com todos os PCs. Não há release estável publicada.

## Funcionalidades integradas

- **Home e hardware:** CPU, GPU, memória, Windows, placa-mãe e BIOS via APIs/WMI. Inventário de DIMMs com dados SMBIOS/WMI, sem afirmar leitura SPD ou topologia de canal.
- **Monitor:** CPU/RAM por APIs nativas; uso 3D por NVIDIA SMI ou contadores Windows GPU Engine. A agregação WMI soma instâncias de processos que compartilham o mesmo engine físico e escolhe o engine 3D mais ocupado, com testes para múltiplos processos e adaptadores. Temperatura, VRAM e potência dependem do suporte do driver NVIDIA; sensores de CPU e fontes AMD/Intel ainda não estão integrados.
- **Benchmark:** PresentMon oficial baixado sob demanda, com SHA-256 fixado, captura ETW, CSV/JSON, FPS médio, 1%/0,1% low, frametimes e comparação A/B de três rodadas por fase. Não promete ganhos por tweak nem controla a cena do jogo.
- **Perfis e energia:** perfis manuais Performance/Equilibrado/AFK e automação por jogo/inatividade. Jogo ativo com entrada recente usa Performance; jogo ou processo protegido em inatividade usa Equilibrado + bloqueio de suspensão; economia AFK só entra sem cargas protegidas após 15 minutos. Roblox e jogos do catálogo, cmd, PowerShell, Python, Node.js, Java, WSH e AutoHotkey são protegidos por padrão. A lista personalizada é somada e não substitui os padrões. Configuração de usuário corrompida ou catálogo embarcado ausente/inválido mantém AFK protegido e exibe aviso; o catálogo embarcado exige schema válido e listas não vazias. A tela inclui inventário somente leitura de processos (nome, PID, memória) para adicionar o nome exato à lista personalizada sem ler comandos/caminhos. Nenhum processo é encerrado; o plano original é exportado, verificado e restaurável.
- **Registro e Debloat:** 11 ajustes HKCU transacionais e reversíveis. Limpeza seletiva de Temp e caches Brave/Discord, preservando perfis e credenciais. Inventários Win32/AppX/MSIX e provisionados são somente leitura. Inicialização HKCU e Startup pessoal têm backup/reversão; itens de máquina e Startup compartilhada são somente leitura.
- **Serviços e tarefas:** whitelist conservadora de seis serviços e seis tarefas opcionais para alterações reversíveis. Toolbox oferece inventário somente leitura de serviços e tarefas agendadas, filtro, classificação, detalhes e CSV.
- **Rede:** inventário de NIC, drivers e propriedades avançadas em modo somente leitura; diagnóstico IPv4/IPv6 ICMP/TCP/DNS/tracert; monitor contínuo com gateways IPv4/IPv6, ICMP/TCP 443, DNS A/AAAA, perda/jitter em 60 segundos e exportação CSV. Energia de NIC só altera opções compatíveis, reversivelmente e sem reiniciar o adaptador.
- **Placa-mãe:** perfil GIGABYTE A520M K V2, inventário PnP e drivers. SMBIOS é apenas indício; BIOS por revisão só aparece após confirmação manual da serigrafia do PCB (1.0/1.1/1.2).
- **Manutenção e recuperação:** DISM/SFC em comandos allowlisted, snapshots locais com SHA-256, backups e restauração de Registro/energia/serviços/NIC/tarefas/inicialização. A bandeja mantém operações ativas quando a janela é fechada.
- **Instalador e distribuição:** .NET 10 self-contained win-x64; versão única no csproj; Inno Setup para C:\ynoez; metadados PE e SHA-256 verificados. A CI audita dependências NuGet, roda testes (incluindo validação estrita do catálogo AFK e inventário de processos), publica o app, instala o setup em runner limpo, reinstala sobre ele, testa desinstalação preservando Data, Backups, Logs e Downloads, valida hashes e publica os dois artefatos.

## Regras de segurança

Permanecem protegidos Microsoft Store/licenciamento, impressão, Calculadora, Clipboard, Notepad, Windows Search, Windows Update, Defender, Firewall, SmartScreen, UAC, NVIDIA Instant Replay/Alt+Z, Vanguard, FACEIT, TPM, Secure Boot, VBS/HVCI e IOMMU/DMA. Não há flash de BIOS, overclock, tensão, alterações BCD/HPET/PlatformTick ou desativação em massa de serviços. AppX não é removido.

## Limites que ainda impedem chamar de release estável

1. Testar em Windows real: abertura, navegação, WMI, sessões longas, PresentMon com jogos reais, rede e recuperação após falhas.
2. Validar Roblox + automações XT por várias horas e observar que processos/scripts permanecem ativos; cadastrar o nome exato do processo XT se ele for independente dos runtimes protegidos.
3. Testar Vanguard/FACEIT em ambiente autorizado e hardware AMD/Intel/NVIDIA reais. A CI não confirma compatibilidade de anti-cheat.
4. Validar versões suportadas de Windows 11 e Windows 10 Enterprise/LTSC listadas pela Microsoft, contas padrão/administrador e cenários sem Internet. Windows 10 Home/Pro não faz parte da matriz suportada do .NET 10.
5. Testar upgrade de uma versão anterior publicada e rollback; o smoke test atual cobre instalação limpa, reinstalação do mesmo setup e desinstalação com retenção de dados.
6. Integrar sensores adicionais somente por APIs/documentação/licenças verificadas, sem instalar drivers de kernel que possam interferir com anti-cheats.
7. Produzir SBOM e assinar Authenticode quando houver certificado válido. Sem certificado, a distribuição só pode ser acompanhada de hashes verificáveis.
8. Refatoração MVVM/DI, journal global e teste de crash-recovery mais abrangente são débitos de arquitetura.

## Política

A CI verde é condição necessária, não suficiente. A versão estável só será marcada após os testes Windows/hardware acima, revisão final do candidato e verificação de todos os artefatos. AppX, firmware e alterações de segurança continuam conservadores por desenho.
