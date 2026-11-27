# Roteiro de engenharia — ynoez até a versão estável

**Atualização 0.2.2:** já existem motor reversível HKCU, inicialização HKCU, whitelist curta de serviços opcionais, perfis de energia automáticos, telemetria CPU/RAM/GPU, PresentMon com CSV, diagnóstico ICMP/TCP/DNS/rota e ajustes seletivos de energia de NIC. Os itens abaixo continuam sendo os critérios restantes; CI não equivale a validação em hardware real.

Não há data artificial de entrega. Uma versão só avança quando passa pelos critérios de qualidade abaixo.

## Fase 0 — Auditoria e especificação (em andamento)
- [x] Inspecionar a fonte 0.1.1 e identificar débitos técnicos.
- [x] Pesquisar implementações públicas de WinUtil, Win11Debloat, Zenith, Optimizer, optimizerDuck, Winhance, AtlasOS e Sophia Script.
- [x] Registrar diferenças de licença e não copiar código GPL para o núcleo MIT.
- [x] Auditar estaticamente o código-fonte público do Stix Free Tweaking Utility (sem licença explícita) e registrar riscos em docs/STIX-SOURCE-AUDIT.md.
- [ ] Extrair e analisar os recursos internos do instalador Stix; o arquivo recebido é um instalador Inno Setup, não o executável do painel em si.
- [ ] Comparar telas/fluxos reais do Stix com screenshots fornecidos e validar fontes/assets licenciáveis. O código público confirma WinForms/Guna/Cuore, mas não substitui screenshots do instalador enviado.

## Fase 1 — Arquitetura e qualidade
- [ ] Migrar code-behind para MVVM com DI e serviços assíncronos.
- [ ] Separar UI, domínio, infraestrutura Windows e catálogo JSON.
- [ ] Criar catálogo versionado de ações com ID estável, build mínimo/máximo, hardware, risco, reinício, pré-condições, detector, plano, apply, verify e undo.
- [x] Criar motor transacional inicial HKCU com backup por valor, verificação e undo; journal global e recuperação após crash ainda pendentes.
- [x] Criar suíte xUnit inicial e pipeline Windows para build, testes e empacotamento; análise estática/SBOM ainda pendentes.
- [ ] Adicionar verificação de integridade/assinatura de artefatos e SBOM.

## Fase 2 — Observabilidade e inventário
- [ ] Inventário detalhado CPU, GPU, RAM/slots/SPD quando permitido, placa/revisão, BIOS, PCIe, monitores, discos, drivers, áudio e NIC.
- [x] Monitoramento inicial CPU/RAM via APIs Windows e GPU 3D via NVIDIA SMI/WMI com indicação de fonte; sensores de temperatura por fabricante ainda pendentes.
- [ ] Health check com problemas reais priorizados (ex.: memória em single channel, XMP/DOCP, refresh rate, driver ausente, plano de energia, temperaturas).
- [ ] Exportar relatório local para suporte e baseline.

## Fase 3 — Toolbox e debloat
- [ ] Lista de apps instalados (Win32, Store/Appx, provisionados) com publisher, versão, tamanho estimado, origem e método oficial de remoção.
- [x] Startup Run keys HKCU reversíveis e serviços opcionais por whitelist; Startup folders e tarefas de logon ainda pendentes.
- [ ] Catálogo de apps opcionais com re-instalação documentada e proteção por dependências.
- [~] Pesquisa unificada de configurações/atalhos, inventário de serviços e tarefas agendadas em tempo real, com filtros, classificação conservadora e CSV. Inventário de recursos opcionais e pesquisa unificada em todos os painéis do Windows ainda pendentes.
- [ ] Debloat de Windows 10/11 separado por build, edição, idioma e perfil do usuário.
- [ ] Perfis Lite / Balanceado / Avançado; modo agressivo sempre desmarcado e com riscos explícitos.
- [ ] Manter Store, Print, Calculator, Clipboard, Notepad, Windows Search, NVIDIA Instant Replay, Defender, Update e componentes anti-cheat protegidos.

## Fase 4 — Jogos e latência
- [x] Detecção inicial por lista configurável de processos e perfis Performance/Equilibrado/AFK; perfis avançados por jogo ainda pendentes.
- [ ] Sessão de jogo com snapshot de processos/serviços alterados, allowlist e restauração ao fechar.
- [ ] Não encerrar scripts, downloads, Roblox, automações XT, overlays necessários, Discord/voice ou processos marcados como essenciais.
- [x] Captura PresentMon com FPS médio, 1% low (P99 inverso), frametime e duração; comparação automática e 0,1% low ainda pendentes.
- [ ] Perfis de GPU por vendor, sem prometer ganhos universais e sem forçar prioridade/afinidade sem evidência.

## Fase 5 — Rede e energia
- [x] Monitoramento persistente ICMP gateway/1.1.1.1, TCP 443 e timestamps; monitor DNS e IPv6 persistente ainda pendentes.
- [x] Diagnóstico pontual ICMP/TCP/DNS IPv4/IPv6/tracert e monitor com perda/jitter/CSV; correlação automática de incidentes ainda pendente.
- [x] Jitter/perda em janela móvel de 60s e tracert sob demanda; percentis e janela configurável ainda pendentes.
- [x] Consulta inicial de Selective Suspend/Device Sleep com alteração reversível quando suportada; driver, EEE, interrupt moderation e advanced properties ainda pendentes.
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


## Energia: integridade do backup base

Antes de cada troca manual de plano, o serviço valida o GUID e o caminho do backup existente. Se uma versão antiga não tiver exportação .pow ou o arquivo tiver sido removido/vazio, o serviço só prossegue depois de confirmar que o plano original ainda está instalado e exportá-lo novamente para Backups. Se não houver plano nem exportação utilizável, a alteração é bloqueada.
