# Desenvolvimento — ynoez 0.2.2

## Implementado nesta linha
- Motor transacional de Registro HKCU com whitelist, backup atômico, detecção, verificação e reversão.
- Catálogo integrado com 11 ajustes opcionais de privacidade, interface e captura Game DVR, incluindo build mínimo do Windows.
- Gerenciador de inicialização: inventário HKCU/HKLM 32/64 bits; desativação reversível somente para entradas não protegidas do usuário atual.
- Catálogo de seis serviços opcionais: DiagTrack, dmwappushservice, MapsBroker, Fax, RetailDemo e WMPNetworkSvc. Backup do modo de inicialização, tratamento de delayed-auto, confirmação, UAC, tentativa de parada sem forçar dependências e restauração verificada.
- Perfis manuais Performance, Equilibrado e AFK, com backup do plano original.
- Perfil automático por jogo conhecido e inatividade do teclado/mouse: Performance em jogo com usuário ativo, Equilibrado no uso comum e Economia após 15 minutos sem entrada. Não encerra processos e roda enquanto o ynoez está aberto.
- Monitor ao vivo de CPU/RAM via APIs nativas e GPU 3D via NVIDIA SMI ou WMI GPU Engine. Temperatura/VRAM/potência dependem do suporte NVIDIA; não instala drivers de sensor.
- Benchmark PresentMon 2.6.0 sob demanda, SHA-256 fixado, captura ETW por PID, CSV, FPS médio, 1% low (inverso do P99 frametime), frametime médio e frames lentos. UAC quando necessário; licenças MIT e avisos de terceiros incluídos.
- Diagnóstico de rede: ICMP, TCP/443, DNS IPv4/IPv6 e tracert.
- Monitoramento contínuo a cada 5 segundos com janela móvel de perda/jitter, ICMP gateway/1.1.1.1, TCP 1.1.1.1:443 e CSV.
- Consulta e ajuste reversível de Selective Suspend/Device Sleep on Disconnect em adaptadores físicos compatíveis, sem reinício automático do adaptador.
- Testes Windows de transações de Registro, tipos originais, backup corrompido, inicialização, parser PresentMon e whitelist de serviços protegidos.
- CI Windows executa testes antes de publicar o executável autocontido.

## Base existente mantida
- Shell WPF com referência visual Stix, Home, Monitor, Backup, Tweaks, Network, Debloat, Motherboard, Toolbox, Restore e Logs.
- Inventário WMI, snapshot local, ponto de restauração opcional e limpeza seletiva Temp/Brave/Discord, preservando perfis e credenciais.
- Perfil GIGABYTE A520M K V2 e política de componentes protegidos.

## Limites conhecidos
- Entradas de inicialização de máquina são somente leitura; serviços opcionais são uma whitelist curta, não há desativação em massa.
- Não há remoção AppX/provisioned, gerenciamento de tarefas agendadas ou recursos opcionais ainda.
- O modo automático roda somente enquanto o ynoez está aberto e deixa o último plano ativo quando é parado.
- A telemetria de GPU depende do driver/Windows; temperatura de CPU e sensores AMD/Intel ainda não estão integrados.
- O benchmark precisa ser testado com jogos reais; PresentMon exige permissão ETW e não mede ganho causado por tweaks sozinho.
- O monitor de rede ajuda a separar ICMP de TCP e gateway de destino, mas não determina sozinho a causa de desconexões do roteador/ISP.
- Ainda falta validação física com Vanguard/FACEIT, instalador/atualizador/desinstalador e teste de recuperação em Windows real.

## Próxima linha de engenharia
1. Comparação automática de benchmarks antes/depois por jogo, 0,1% low e sessões repetidas.
2. Inventário de AppX/Win32/provisionados e recursos opcionais, com restauração comprovada.
3. Tarefas agendadas e serviços adicionais somente com análise de dependências e rollback.
4. Diagnóstico de NIC mais completo (driver, EEE, interrupt moderation e propriedades avançadas) com recomendações condicionais.
5. Migrar code-behind para MVVM/DI e ampliar testes de falhas, concorrência e recuperação após crash.
6. Instalador, atualizador, desinstalador, assinatura/hash e validação em hardware real.
