# ynoez 0.3.1 — candidato integrado

Esta versão reúne a navegação modular e os diagnósticos já existentes no ynoez com atalhos de otimização, debloat, foco de jogo, compatibilidade de firmware e a abertura vetorial do sigilo.

## O que foi integrado
- Painel lateral preservando Home, Monitor, Backup, Tweaks, Network, Debloat, Motherboard, Toolbox, Restore e Logs.
- Atalhos rápidos para Otimizar agora, Debloat automático, Foco de Jogo e BIOS / anti-cheat.
- Abertura com sigilo em partículas, desenho vetorial nítido e opção de pular por clique.
- Perfil de otimização conservador, com backup de Registro, MMCSS Games e plano de energia; rotina explícita de restauração.
- Debloat baseado em allowlists: aplicações AppX opcionais, serviços e tarefas selecionados, além de limpeza limitada de caches.
- Foco de jogo com prioridade configurável, detecção CS2/Roblox e proteção contra uso de prioridade Realtime.
- Diagnóstico de firmware e adaptações limitadas a Lenovo WMI e ASUS ACT quando a ferramenta/modelo forem compatíveis.
- Correção da correspondência de fabricante/placa para strings SMBIOS como “Gigabyte Technology Co., Ltd.”.
- Requisitos de firmware de anti-cheat separados do ajuste independente de boost da CPU.
- CI acionada em pull requests, build self-contained Windows x64, instalador Inno Setup e artefatos de checksum.

## Segurança e compatibilidade
- Não desativa Defender, Firewall, Windows Update, UAC, Microsoft Store, Xbox, serviços de rede, Bluetooth, Wi-Fi, áudio ou impressão em massa.
- Não aplica offsets UEFI genéricos, não faz flash de BIOS e não altera tensão, clocks ou limites térmicos.
- Gravação de BIOS é bloqueada se a condição do BitLocker for desconhecida e exige confirmação explícita.
- Em placas sem interface de configuração suportada, a aplicação fornece instruções manuais; não promete escrita automática.
- O login de abertura é demonstrativo/local e não protege o aplicativo com autenticação real.
- A remoção de aplicativos AppX não tem restauração automática; a tela informa essa limitação antes da execução.

## Validação ainda necessária
Este commit precisa passar pela CI de Windows antes de ser tratado como instalador pronto. Mesmo com CI verde, o funcionamento de firmware, anti-cheats, prioridades de processo, monitoramento e ganhos reais de FPS precisa de teste no PC de destino. Não há garantia universal de compatibilidade nem promessa de aumento de FPS.

