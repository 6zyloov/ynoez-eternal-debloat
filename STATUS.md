# Desenvolvimento — ynoez 0.2.0

## Implementado nesta linha
- Motor transacional de ajustes de Registro para HKCU, com whitelist de caminhos, backup atômico, detecção, verificação pós-aplicação e restauração.
- Catálogo inicial integrado à interface Tweaks, sem aplicação automática e com confirmação por ação.
- Restauração de tipos originais DWORD, QWORD, texto, texto expansível, binário e multi-string.
- Gerenciador de inicialização: inventário HKCU/HKLM 32/64 bits; desativação reversível somente para entradas não protegidas do usuário atual.
- Proteção de entradas que aparentam pertencer a segurança, NVIDIA, drivers, áudio, rede e anti-cheats.
- Monitoramento de rede contínuo a cada 5 segundos, com CSV persistente, cancelamento e status do gateway/1.1.1.1.
- Cancelamento real nas sondagens ICMP do diagnóstico.
- Testes de integração Windows para aplicar/restaurar DWORD existente, remover valor criado, carregar catálogo e desativar/restaurar inicialização.
- CI Windows executa testes antes de publicar o binário.

## Base existente mantida
- Shell WPF com referência visual Stix, Home, Backup, Tweaks, Network, Debloat, Motherboard, Toolbox, Restore e Logs.
- Inventário WMI, snapshot local, ponto de restauração opcional e plano de energia com reversão.
- Limpeza seletiva de Temp e caches Brave/Discord, com preservação de perfis e credenciais.
- Perfil GIGABYTE A520M K V2 e política de componentes protegidos.

## Limites conhecidos
- Entradas de inicialização de máquina são somente leitura nesta versão; não há desativação de serviços ou tarefas em massa.
- O catálogo inicial é pequeno e contém ajustes de risco baixo/médio; não representa um conjunto completo de otimizações.
- Monitoramento atual usa ICMP para gateway e 1.1.1.1. Não mede perda TCP/UDP de aplicativos, rota completa, jitter de áudio ou causa definitiva de desconexões.
- Ainda não há sensores contínuos CPU/GPU, benchmark PresentMon, modo automático de jogo/AFK, gerenciamento AppX/serviços/tarefas ou instalador final.
- A CI compila e testa em runner Windows, mas não equivale a validação na máquina física do proprietário nem prova compatibilidade Vanguard/FACEIT.

## Próxima linha de engenharia
1. Testes de falha e concorrência no motor transacional.
2. Catálogo expandido com evidência, compatibilidade por build e reversão individual.
3. Gestão segura de aplicativos provisionados, tarefas e serviços com privilégio mínimo.
4. Modos Performance/Equilibrado/AFK com lista de processos protegidos e restauração garantida.
5. Métricas CPU/GPU, PresentMon e comparações antes/depois.
6. Diagnóstico de rota, TCP, IPv4/IPv6 e rede persistente com sumarização de incidentes.
7. Instalador, atualizador, desinstalador, assinatura/hash e validação em hardware real.
