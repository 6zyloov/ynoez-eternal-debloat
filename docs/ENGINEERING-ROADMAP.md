# Roteiro de engenharia — ynoez 0.3.0 até a versão estável

O candidato integrado reúne o aplicativo atual em PR draft #12. A CI usa Windows Server 2025. Uma compilação verde não equivale a teste em Windows 11 cliente nem a teste em hardware real.

## Fase 0 — auditoria, referências e licença

- [x] Auditar código-base e registrar débitos técnicos.
- [x] Pesquisar ferramentas públicas de tuning/debloat e comparar abordagens sem copiar código incompatível com a licença MIT do núcleo.
- [x] Registrar limites legais do código Stix sem licença explícita; manter identidade própria do ynoez.
- [ ] Inspecionar os recursos internos do instalador Stix e comparar com screenshots/fonte visual original, caso os materiais completos estejam disponíveis.
- [x] Incorporar fontes Poppins com licença OFL e aviso correspondente.

## Fase 1 — arquitetura e qualidade

- [x] Projeto WPF nativo net10.0-windows, Nullable e build self-contained win-x64.
- [x] Suíte xUnit e CI Windows para testes, publicação e empacotamento.
- [x] Auditoria de vulnerabilidades NuGet diretas/transitivas; avisos NU1901–NU1904 são erros de restore.
- [x] Motor transacional inicial de Registro HKCU com backup, verificação e reversão.
- [ ] Refatorar MainWindow.xaml.cs para MVVM/DI, separando UI, domínio, infraestrutura Windows e catálogos.
- [ ] Journal global transacional e recuperação de todas as operações após crash, além da recuperação específica de energia.
- [x] Pipeline de geração e validação de SBOM SPDX 2.2; revisão formal de licenças continua separada.
- [x] Pipeline de assinatura Authenticode opcional com verificação e estado explícito sem certificado; assinatura efetiva depende de certificado Code Signing válido.

## Fase 2 — observabilidade e inventário

- [x] CPU/RAM por APIs nativas; GPU NVIDIA SMI ou contadores Windows GPU Engine com agregação por engine e testes.
- [x] Inventário de hardware/WMI, placa/BIOS, DIMMs SMBIOS e drivers PnP de rede/áudio/sistema.
- [x] Inventário de serviços e tarefas agendadas; NIC avançada em modo somente leitura.
- [ ] Temperatura CPU e fontes adicionais AMD/Intel por APIs oficialmente documentadas, sem driver de kernel vulnerável.
- [ ] Inventário mais profundo de monitores/refresh rate, discos/NVMe, PCIe e sensores de fabricante, com indicação clara do que está indisponível.
- [ ] Relatório de saúde priorizado e exportável com baseline e comparação entre sessões.

## Fase 3 — Toolbox e Debloat

- [x] Inventário de apps Win32, AppX/MSIX e pacotes provisionados somente leitura, com filtros e CSV.
- [x] Startup pessoal/HKCU reversível; itens HKLM e Startup compartilhada somente leitura.
- [x] Whitelist curta de serviços/tarefas opcionais com backup e restauração; inventários completos somente leitura.
- [x] Limpeza seletiva Temp/Brave/Discord preservando perfis e credenciais.
- [ ] Classificação mais profunda por versão, publisher, dependências e origem oficial de instalação.
- [ ] Remoção AppX/provisioned somente após mecanismo confiável de reaquisição, rollback por pacote e validação multiusuário; até lá permanece desativada.
- [ ] Recursos opcionais e perfis Debloat separados por build/edição/idioma, com modo agressivo sempre desligado por padrão.

## Fase 4 — jogos, processos e performance

- [x] Perfis Performance/Equilibrado/AFK com restauração do plano original.
- [x] AFK protegido: jogos e runtimes comuns de automação protegidos por padrão; lista personalizada soma sem apagar a lista embarcada; catálogo embarcado exige schema e listas válidas; config ausente/corrompida falha de forma segura e mostra aviso.
- [x] Inventário somente leitura de processos em execução (nome, PID e memória) com seleção para adicionar o nome à lista protegida; sem linha de comando, caminhos ou manipulação de processos.
- [x] PresentMon com métricas de frametime/FPS, 1%/0,1% low e sessões A/B.
- [ ] Catálogo de processos por jogo atualizado e revisado, sem supor nomes de executáveis XT desconhecidos.
- [ ] Comparações A/B controladas por jogo com registro de cena, resolução, preset, driver, temperatura, processos concorrentes e variação entre rodadas.
- [ ] Recomendações condicionais de GPU/energia baseadas em medições; sem prometer ganho e sem mudar prioridade/afinidade automaticamente.

## Fase 5 — rede e energia

- [x] Diagnóstico ICMP/TCP/DNS/tracert IPv4/IPv6.
- [x] Monitor contínuo IPv4/IPv6 com gateway, destinos externos, DNS A/AAAA, perda/jitter por família/protocolo e CSV.
- [x] Auditoria avançada de propriedades do driver NIC, somente leitura.
- [x] Ajustes compatíveis de energia de NIC com backup/reversão e sem reinício automático.
- [ ] Correlação de incidentes entre timestamps, perda, gateways, DNS e mudança de interface.
- [ ] Mais testes de longa duração em Wi-Fi e Ethernet, IPv4/IPv6 dual-stack e redes com ICMP bloqueado.
- [ ] Recomendações de EEE/Interrupt Moderation/RSS/LSO condicionais a driver, link e medição; continuar sem aplicar propriedades avançadas automaticamente.

## Fase 6 — central de hardware

- [x] Perfil GIGABYTE A520M K V2 com fontes oficiais, utilitários e políticas de segurança.
- [x] Separar revisão SMBIOS (indício) de revisão física confirmada; bloquear link de BIOS por revisão até confirmação manual do PCB 1.0/1.1/1.2.
- [x] Inventário PnP e metadados dos drivers para rede/áudio/sistema.
- [ ] Cruzar CPU instalada com lista oficial de suporte e versão mínima de BIOS por revisão, sem inferir de SMBIOS quando faltar prova.
- [ ] Identificar sensores/header de fan compatíveis antes de exibir qualquer ação Smart Fan; por enquanto, monitoramento/links apenas.
- [ ] Adicionar outras placas apenas quando fabricante/modelo/revisão e documentação oficial estiverem verificados.

## Fase 7 — instalação, compatibilidade e release

- [x] Instalador Inno Setup e pacote portátil com versão única, metadados PE e SHA-256.
- [x] CI Windows testa instalação limpa, reinstalação sobre a mesma instalação e desinstalação preservando Data, Backups, Logs e Downloads.
- [x] CI testa upgrade do baseline atual de main para o candidato, rollback para o baseline, re-upgrade e retenção de dados; a migração de uma release estável publicada continuará pendente até existir uma versão anterior distribuída.
- [x] Pipeline de release por tag executa testes, gera SBOM SPDX 2.2 e cria status Authenticode verificável; assinatura real exige certificado.
- [ ] Validar versões suportadas de Windows 11 e Windows 10 Enterprise/LTSC em máquinas clientes reais, conta padrão/administrador, com/sem Internet e diferentes escalas de DPI. Windows 10 Home/Pro não está na matriz oficial do .NET 10.
- [ ] Validar hardware AMD/Intel/NVIDIA e sessões longas com Roblox + XT.
- [ ] Testar Vanguard/FACEIT em ambiente autorizado, sem tentar contornar anti-cheat.
- [ ] Testar uma migração real entre duas releases públicas depois da primeira versão estável.
- [ ] Revisão formal de licenças e revisão manual final, promover o PR consolidado e somente então criar tag/release, com autorização do dono do repositório.

## Regras que não serão flexibilizadas

- Não encerrar processos, jogos, scripts, downloads ou automações XT.
- Preservar Store/licenciamento, impressão, Calculadora, Clipboard, Notepad, Search, Update, Defender, Firewall, SmartScreen, UAC e NVIDIA Instant Replay/Alt+Z.
- Preservar Vanguard/FACEIT, TPM, Secure Boot, VBS/HVCI, IOMMU/DMA.
- Não aplicar BCD/HPET/PlatformTick placebo, BIOS flash, overclock, tensão ou curva de fan automática.
- AppX permanece somente leitura enquanto recuperação/rollback não forem realmente confiáveis.
- Não declarar final somente com base em CI verde.

## Critério de saída

A versão só sai de candidata quando os testes automatizados e os testes em Windows/hardware real estiverem documentados, instalação/migração/rollback/desinstalação estiverem validados, artefatos estiverem auditáveis e os limites restantes forem aceitos explicitamente.
