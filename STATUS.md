# Desenvolvimento — ynoez 0.3.0

- Base de build atualizada para .NET 10 LTS (`net10.0-windows`), usando `System.Management 10.0.15`.

## Implementado nesta linha
- Motor transacional de Registro HKCU com whitelist, backup atômico, detecção, verificação e reversão.
- Catálogo integrado com 11 ajustes opcionais de privacidade, interface e captura Game DVR, incluindo build mínimo do Windows.
- Inventário Win32: leitura de entradas HKLM/HKCU em registros 32/64 bits, filtro por nome/editor/classificação, exportação CSV com neutralização de fórmula; não executa UninstallString nem remove aplicativos.
- Inventário AppX/MSIX atual por usuário e inventário administrativo de todos os perfis + pacotes provisionados, com classificação de componentes centrais, UAC explícito e exportação CSV; nenhum pacote é removido.
- Gerenciador de inicialização: inventário HKCU/HKLM 32/64 bits e pastas Startup pessoal/compartilhada; entradas HKCU não protegidas e arquivos pessoais podem ser desativados com backup/reversão; HKLM e Startup compartilhada somente leitura.
- Catálogo de seis serviços opcionais: DiagTrack, dmwappushservice, MapsBroker, Fax, RetailDemo e WMPNetworkSvc. Backup do modo de inicialização, tratamento de delayed-auto, confirmação, UAC, tentativa de parada sem forçar dependências e restauração verificada.
- Perfis manuais Performance, Equilibrado e AFK, com backup do plano original.
- Perfil automático por jogo conhecido e inatividade do teclado/mouse: Performance durante jogos com entrada ativa, Equilibrado no uso comum e AFK após 15 minutos sem entrada. Não encerra processos. Exporta o plano original e restaura ao parar; tenta recuperar sessões interrompidas na próxima inicialização. AFK bloqueia suspensão automática enquanto o app está aberto, sem impedir que a tela apague.
- Monitor ao vivo de CPU/RAM via APIs nativas e GPU 3D via NVIDIA SMI ou WMI GPU Engine. Temperatura/VRAM/potência dependem do suporte NVIDIA; não instala drivers de sensor.
- Benchmark PresentMon 2.6.0 sob demanda, SHA-256 fixado, captura ETW por PID, CSV + resumo JSON, FPS médio, 1% low (inverso do P99), 0,1% low (inverso do P99,9), frametime médio, p99/p99,9 e frames lentos. Sessões A/B com três rodadas em cada fase, mediana, dispersão e JSON persistente. A série não controla a cena, mapa, temperatura ou processos do PC.
- Verificador manual de atualização: consulta somente quando solicitado o endpoint de release estável oficial do GitHub, valida tag/URL/repositório e apenas abre a página oficial quando há versão mais nova; não baixa nem instala automaticamente. UAC quando necessário; licenças MIT e avisos de terceiros incluídos.
- Diagnóstico de rede: ICMP, TCP/443, DNS IPv4/IPv6 e tracert.
- Monitoramento contínuo a cada 5 segundos com gateway IPv4 dinâmico, ICMP/TCP para Cloudflare e Google, resolução DNS, janela móvel real de 60 segundos e hipótese de causa, com exportação CSV.
- Inventário de adaptadores físicos e virtuais com endereços, DNS, gateways, MTU e versão de driver quando o WMI informa; exportação CSV, somente leitura. Consulta e ajuste reversível de Selective Suspend/Device Sleep on Disconnect em adaptadores físicos compatíveis, sem reinício automático do adaptador.
- Central Restore consolida recuperação de Registro, plano base/AFK interrompido, serviços opcionais, adaptadores de rede, tarefas, inicialização e snapshots. Backups órfãos ou corrompidos permanecem preservados.
- Testes Windows de transações de Registro, tipos originais, backups corrompidos, inicialização HKCU e pasta Startup temporária, fallback de energia, manifesto SHA-256 de snapshots, métricas da janela de rede, parser PresentMon e whitelists de serviços/tarefas.
- CI Windows executa testes antes de publicar o executável autocontido.
- Verificador manual de atualização consulta somente quando solicitado a release estável do repositório GitHub, valida tag/URL e abre a página oficial; não baixa nem instala silenciosamente.
- Pipeline separada de release por tag vX.Y.Z, com teste Windows, instalador Inno Setup, pacote portátil e manifesto SHA-256.

## Base existente mantida
- Shell WPF com referência visual Stix, Home, Monitor, Backup, Tweaks, Network, Debloat, Motherboard, Toolbox, Restore e Logs.
- Inventário WMI, snapshot local, ponto de restauração opcional e limpeza seletiva Temp/Brave/Discord, preservando perfis e credenciais.
- Perfil GIGABYTE A520M K V2 e política de componentes protegidos.

## Limites conhecidos
- Entradas de inicialização de máquina são somente leitura; serviços opcionais são uma whitelist curta, não há desativação em massa.
- Não há remoção AppX/provisioned nem gerenciamento de recursos opcionais. Tarefas agendadas limitadas a seis itens de telemetria/feedback/mapas; manutenção, Update, Defender, indexação, drivers e anti-cheats permanecem fora da whitelist.
- O modo automático roda somente enquanto o ynoez está aberto; ao parar, tenta restaurar o plano original. Se falhar, mantém metadados e exportação .pow para nova tentativa na abertura seguinte. O bloqueio de suspensão termina quando o app fecha.
- A telemetria de GPU depende do driver/Windows; temperatura de CPU e sensores AMD/Intel ainda não estão integrados.
- O benchmark precisa ser testado com jogos reais; PresentMon exige permissão ETW e não mede ganho causado por tweaks sozinho.
- O monitor de rede ajuda a separar ICMP de TCP e gateway de destino, mas não determina sozinho a causa de desconexões do roteador/ISP.
- Ainda falta validação física com Vanguard/FACEIT, assinatura Authenticode e teste de recuperação em Windows real. O verificador de versões não substitui assinatura nem atualização automática.

## Limites adicionais
- Snapshots locais são inventários de hardware/energia/rede com hashes de integridade; não são uma imagem completa do Windows nem substituem backup externo.
- Alguns Windows não possuem SCHEME_MIN ou SCHEME_MAX; o fallback para Equilibrado é explícito e o perfil AFK ainda ativa o bloqueio de suspensão automática.
- Poppins é incorporada sob SIL Open Font License; o texto integral está em `src/ynoez/Assets/Fonts/OFL.txt`.

## Próxima linha de engenharia
1. Comparação A/B repetida e controlada de benchmarks por jogo, agregação de várias sessões e controle/registro de cena, temperaturas e processos concorrentes.
2. Inventário AppX/Win32/provisionados e remoção só quando houver pacote de recuperação confiável.
3. Diagnóstico de NIC mais completo (driver, EEE, interrupt moderation e propriedades avançadas) com recomendações condicionais.
4. Migrar code-behind para MVVM/DI e ampliar testes de falhas, concorrência e recuperação após crash.
5. Atualizador com validação criptográfica, assinatura Authenticode e testes físicos em hardware real.
