# Roteiro de engenharia — ynoez até a versão estável

Não há data artificial de entrega. Uma versão só avança quando passa pelos critérios de qualidade abaixo.

## Fase 0 — Auditoria e especificação (em andamento)
- [x] Inspecionar a fonte 0.1.1 e identificar débitos técnicos.
- [x] Pesquisar implementações públicas de WinUtil, Win11Debloat, Zenith, Optimizer, optimizerDuck, Winhance, AtlasOS e Sophia Script.
- [x] Registrar diferenças de licença e não copiar código GPL para o núcleo MIT.
- [ ] Extrair e analisar os recursos internos do instalador Stix; o arquivo recebido é um instalador Inno Setup, não o executável do painel em si.
- [ ] Comparar telas/fluxos reais do Stix com screenshots fornecidos e validar fontes/assets licenciáveis.

## Fase 1 — Arquitetura e qualidade
- [ ] Migrar code-behind para MVVM com DI e serviços assíncronos.
- [ ] Separar UI, domínio, infraestrutura Windows e catálogo JSON.
- [ ] Criar catálogo versionado de ações com ID estável, build mínimo/máximo, hardware, risco, reinício, pré-condições, detector, plano, apply, verify e undo.
- [ ] Criar motor transacional com journal, snapshots por ação, undo idempotente, recuperação após crash e dry-run.
- [ ] Criar suíte xUnit e pipeline Windows para build, análise, testes e empacotamento.
- [ ] Adicionar verificação de integridade/assinatura de artefatos e SBOM.

## Fase 2 — Observabilidade e inventário
- [ ] Inventário detalhado CPU, GPU, RAM/slots/SPD quando permitido, placa/revisão, BIOS, PCIe, monitores, discos, drivers, áudio e NIC.
- [ ] Monitoramento ao vivo com providers por fabricante e indicação de fonte/precisão.
- [ ] Health check com problemas reais priorizados (ex.: memória em single channel, XMP/DOCP, refresh rate, driver ausente, plano de energia, temperaturas).
- [ ] Exportar relatório local para suporte e baseline.

## Fase 3 — Toolbox e debloat
- [ ] Lista de apps instalados (Win32, Store/Appx, provisionados) com publisher, versão, tamanho estimado, origem e método oficial de remoção.
- [ ] Startup: Run keys, Startup folders, tarefas de logon e serviços; toggle reversível quando possível.
- [ ] Catálogo de apps opcionais com re-instalação documentada e proteção por dependências.
- [ ] Pesquisa unificada de configurações, serviços, tarefas, recursos e painéis do Windows.
- [ ] Debloat de Windows 10/11 separado por build, edição, idioma e perfil do usuário.
- [ ] Perfis Lite / Balanceado / Avançado; modo agressivo sempre desmarcado e com riscos explícitos.
- [ ] Manter Store, Print, Calculator, Clipboard, Notepad, Windows Search, NVIDIA Instant Replay, Defender, Update e componentes anti-cheat protegidos.

## Fase 4 — Jogos e latência
- [ ] Detecção de jogos/launchers e perfil por jogo.
- [ ] Sessão de jogo com snapshot de processos/serviços alterados, allowlist e restauração ao fechar.
- [ ] Não encerrar scripts, downloads, Roblox, automações XT, overlays necessários, Discord/voice ou processos marcados como essenciais.
- [ ] Benchmarks via PresentMon: FPS médio, 1%/0,1% lows, frametime e duração; comparar cenários repetidos.
- [ ] Perfis de GPU por vendor, sem prometer ganhos universais e sem forçar prioridade/afinidade sem evidência.

## Fase 5 — Rede e energia
- [ ] Monitoramento persistente de gateway, DNS, múltiplos destinos e IPv4/IPv6 com timestamps.
- [ ] Separar perda local, Wi-Fi, gateway, DNS, rota e destino; exportar CSV/JSON.
- [ ] Jitter/latência com percentis e janela configurável; traceroute sob demanda.
- [ ] Inventário de NIC, driver, energia, EEE, interrupt moderation e advanced properties; mostrar valor atual e recomendação condicionada.
- [ ] Perfis de energia Performance/Balanced/AFK sem matar processos e com retorno automático seguro.
- [ ] Diagnóstico de desconexões de madrugada/AFK sem alegar que o app controla quedas do ISP/roteador.

## Fase 6 — Central de hardware
- [ ] Banco por fabricante/modelo/revisão física, com fontes oficiais, BIOS, drivers e utilitários.
- [ ] Primeira placa: GIGABYTE A520M K V2; revisão do PCB precisa ser confirmada.
- [ ] Identificar CPU instalada e suporte mínimo de BIOS; exibir aviso de risco antes de qualquer recomendação de firmware.
- [ ] Integração com APP Center, EasyTune, SIV/Smart Fan 5 e Q-Flash apenas como links/ações oficiais e com detecção de instalação.
- [ ] Nunca flashar BIOS, alterar tensão/clock/PBO ou desativar TPM/Secure Boot/VBS/HVCI/IOMMU automaticamente.
- [ ] Expandir para placas com documentação oficial e demanda real, não gerar perfis genéricos por nome.

## Fase 7 — Release estável
- [ ] UI final alinhada ao Stix e acessível em 100/125/150/200% de escala.
- [ ] Teste Windows 10 22H2 e Windows 11 builds suportadas, incluindo conta sem admin e com/sem Internet.
- [ ] Testes em hardware AMD/Intel e NVIDIA/AMD/Intel GPU.
- [ ] Testes anti-cheat em ambiente autorizado; não tentar contornar mecanismos anti-cheat.
- [ ] Instalação, atualização, desinstalação e recuperação testadas.
- [ ] Assinatura Authenticode se houver certificado; caso contrário, hash público e instrução de verificação.
- [ ] Release notes, SBOM, licenças de terceiros e hashes SHA-256.

## Política de release
- Não chamar uma build de “final” por compilar.
- Cada fase gera builds de teste, com changelog e limitações.
- A versão estável só será marcada quando os critérios de aceite forem demonstrados em CI e em máquinas Windows reais.
