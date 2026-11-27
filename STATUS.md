# Desenvolvimento — ynoez 0.2.1

## Implementado nesta linha
- Motor transacional de ajustes de Registro HKCU, whitelist de caminhos, backup atômico, detecção, verificação e reversão.
- Catálogo integrado com 11 ajustes opcionais de privacidade, interface e captura Game DVR, incluindo compatibilidade mínima por build do Windows.
- Gerenciador de inicialização: inventário HKCU/HKLM 32/64 bits; desativação reversível somente para entradas não protegidas do usuário atual.
- Proteção de entradas que aparentam pertencer a segurança, NVIDIA, drivers, áudio, rede e anti-cheats.
- Perfis manuais Performance, Equilibrado e AFK, com backup do plano original e restauração verificada.
- Perfil automático por processo de jogo conhecido e inatividade do teclado/mouse: Performance em jogo com usuário ativo, Equilibrado no uso comum e Economia após 15 minutos sem entrada. Não encerra processos.
- Monitor ao vivo de CPU e RAM via APIs nativas do Windows; GPU 3D via NVIDIA SMI ou WMI GPU Engine; temperatura, VRAM e potência quando NVIDIA SMI oferece dados.
- Monitoramento de rede contínuo a cada 5 segundos, CSV persistente e cancelamento.
- Testes de integração Windows para transações de Registro, catálogo e inicialização.
- CI Windows executa testes antes de publicar o executável autocontido.

## Base existente mantida
- Shell WPF com referência visual Stix, Home, Monitor, Backup, Tweaks, Network, Debloat, Motherboard, Toolbox, Restore e Logs.
- Inventário WMI, snapshot local, ponto de restauração opcional e limpeza seletiva Temp/Brave/Discord.
- Perfil GIGABYTE A520M K V2 e política de componentes protegidos.

## Limites conhecidos
- Entradas de inicialização de máquina são somente leitura; não há desativação em massa de serviços ou tarefas.
- O catálogo atual é pequeno e cada ajuste futuro exige justificativa, build compatível, detecção e reversão.
- O modo automático usa lista configurável de nomes de processos e tempo de inatividade; roda somente enquanto o ynoez está aberto e deixa o último plano ativo quando é parado.
- A telemetria de GPU depende do driver e do suporte do Windows; temperatura de CPU não está disponível pelo método nativo genérico usado aqui.
- Monitoramento de rede atual usa ICMP para gateway e 1.1.1.1; não prova sozinho a causa de desconexões.
- Ainda não há PresentMon/FPS 1% lows, gerenciamento AppX/serviços/tarefas, instalador/atualizador nem validação física com Vanguard/FACEIT.

## Próxima linha de engenharia
1. Benchmark de FPS/frametime com PresentMon, sessões antes/depois e CSV.
2. Diagnóstico de rede avançado: rota, TCP, IPv4/IPv6, DNS e sumarização de incidentes.
3. Inventário de AppX e recursos opcionais, com política explícita de preservação e restauração.
4. Catálogo de serviços e tarefas com privilégio mínimo, lista de proteção e reversão.
5. Arquitetura modular da interface e testes adicionais de falhas/recuperação.
6. Instalador, atualizador, desinstalador, assinatura/hash e validação em hardware real.
