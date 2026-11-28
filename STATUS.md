# Status do projeto — ynoez 0.3.0

**Situação:** candidato em revisão na PR draft #12. A revisão de código `4388929e99a1308b5b9055486232c50fd5d5123e` passou pela CI completa em Windows Server 2025 ([run 46103835497](https://github.com/6zyloov/ynoez-eternal-debloat/actions/runs/46103835497)). Auditoria NuGet, testes xUnit, publicação, instalador, SBOM, assinatura/status, instalação limpa, reinstalação, desinstalação, upgrade do baseline main, rollback, re-upgrade, retenção de dados e checksums concluíram com sucesso. Não há release estável publicada.

## Interface, otimização e correções da revisão final

- Tema monocromático aplicado globalmente: preto absoluto no fundo, cartões quase pretos, branco de alto contraste e detalhes brancos; o destaque rosa foi removido.
- Botões, ComboBox/itens, TextBox, ListBox/itens, CheckBox, RadioButton, menus, tooltips e DataGrid receberam estilos escuros consistentes. Itens selecionados permanecem escuros, com texto branco e contorno claro.
- Layout esticável e responsivo, sem largura mínima artificial em todos os containers.
- Corrigido o crash WPF reportado ao abrir Tweaks: o mesmo controle visual de status era adicionado duas vezes ao mesmo painel.
- Foco de Jogo detecta automaticamente CS2 em execução/bibliotecas Steam e a versão mais recente do Roblox instalada. Seleção manual permanece como fallback.

## Interface, otimização e correções da revisão atual

- Home reduzida a quatro indicadores de hardware, ações rápidas e informações essenciais; inventário de DIMMs, auditoria longa e atualização não ocupam mais o painel principal.
- Tema global monocromático preto/branco corrigido: o rótulo de botões agora herda a cor do próprio botão, evitando branco sobre branco. Listas, seletores e campos permanecem escuros.
- Tweaks reorganizado para expor primeiro uma ação de **Otimização para jogos** e o Foco de Jogo, deixando o controle automático de energia separado.
- A otimização rápida aplica cinco ajustes reversíveis: desativa captura Game DVR do Windows, define preferência visual de desempenho e desliga animações/transparência da conta atual. Também tenta Alto desempenho somente quando nenhum outro perfil de energia do ynoez já possui backup. O diário registra o que o próprio perfil alterou.
- A otimização rápida não desativa NVIDIA Instant Replay, OBS, drivers, serviços críticos ou anti-cheats; não promete um aumento universal de FPS.
- Debloat não bloqueia mais a navegação: leitura de inicialização, serviços e tarefas ocorre fora da thread da interface. A página mostra status enquanto consulta em segundo plano.
- **Remoção AppX real:** lista fechada de aplicativos opcionais conhecidos, com seleção múltipla e remoção apenas para o usuário atual. Frameworks, recursos, bundles, pacotes não removíveis e componentes essenciais não são elegíveis. Não remove pacotes provisionados nem altera outros perfis. A ação não tem rollback automático; reinstalação depende de disponibilidade na Microsoft Store.
- Limpeza temporária e caches Brave/Discord permanecem seletivos; inicialização, serviços e tarefas opcionais continuam com backup para restauração.

## Funcionalidades integradas

- **Home e hardware:** CPU, GPU, memória, Windows, placa-mãe e BIOS via APIs/WMI. Inventário de DIMMs com dados SMBIOS/WMI, sem afirmar leitura SPD ou topologia de canal.
- **Monitor:** CPU/RAM por APIs nativas; uso 3D por NVIDIA SMI ou contadores Windows GPU Engine. A agregação WMI soma instâncias de processos que compartilham o mesmo engine físico e escolhe o engine 3D mais ocupado, com testes para múltiplos processos e adaptadores. Temperatura, VRAM e potência dependem do suporte do driver NVIDIA; sensores de CPU e fontes AMD/Intel ainda não estão integrados.
- **Foco de Jogo (novo):** detecção automática do CS2 em execução/nas bibliotecas Steam e da versão mais recente do Roblox; seleção manual permanece disponível. Uma única entrada de RobloxPlayerBeta.exe cobre todas as instâncias.  aplicação contínua a todas as instâncias com o mesmo nome. O jogo usa CPU Alta (com confirmação) ou Acima do normal e memória Normal; bots usam CPU Idle (extremamente baixa) ou Abaixo do normal e memória Very Low. O Windows recebe preferência de GPU de alto desempenho para o jogo e economia para bots, quando o hardware oferece múltiplos adaptadores. Alterações ficam journalizadas e são revertidas ao desativar/sair; a janela permanece na bandeja enquanto o perfil estiver ativo. A configuração é salva, mas requer ativação manual em cada execução; a saída normal restaura prioridades e não reaplica o perfil automaticamente. Não há reserva de RAM/VRAM ou controle de percentuais de GPU.
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

1. A CI automatizada desta revisão está verde; ainda falta validação em Windows cliente e hardware físico, porque runner Windows Server 2025 não reproduz o ambiente real de jogo.
2. Testar em Windows cliente: abertura, navegação, WMI, sessões longas, PresentMon em jogos reais, rede e recuperação após falhas.
3. Validar Roblox + automações XT por várias horas, observando que processos e scripts permanecem ativos; cadastrar o nome exato de processos XT independentes dos runtimes protegidos.
4. Testar Vanguard/FACEIT em ambiente autorizado e hardware AMD/Intel/NVIDIA reais. A CI não confirma compatibilidade de anti-cheat.
5. Validar versões suportadas de Windows 11 e Windows 10 Enterprise/LTSC listadas pela Microsoft, contas padrão/administrador e cenários sem Internet. Windows 10 Home/Pro não faz parte da matriz suportada do .NET 10.
6. A CI agora testa upgrade/rollback entre a branch main atual e o candidato; ainda não existe uma versão estável publicada para testar migração de uma release anterior.
7. Assinatura Authenticode real depende de certificado Code Signing válido e secrets configurados. Sem eles, a distribuição declara NOT SIGNED e depende dos hashes SHA-256.
8. Foco de Jogo foi integrado, mas precisa de teste prolongado em CS2 + duas instâncias Roblox + XT no PC de destino; prioridade Idle pode causar desconexões em jogos AFK, caso em que o modo Abaixo do normal deve ser usado. High CPU tem risco de prejudicar áudio/responsividade. GPU é preferência de adaptador e RAM é prioridade de páginas, não reserva.

## Política

CI verde é condição necessária, não suficiente. A versão estável só será marcada após testes Windows/hardware real documentados, a instalação/migração/rollback/desinstalação validados e a revisão final dos artefatos. AppX, firmware e alterações de segurança continuam conservadores por desenho.
