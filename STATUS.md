# Desenvolvimento — ynoez 0.1.1

## Incluído
- Shell WPF com referência visual do Stix: fundo #121212, cards #1E1E1E, destaque #FF80FF, Poppins e sidebar.
- Home, Backup, Tweaks, Network, Debloat, Motherboard, Toolbox, Restore e Logs.
- Inventário WMI de CPU, GPU, RAM, Windows, placa-mãe, BIOS e adaptadores.
- Snapshot local e ponto de restauração opcional.
- Plano Alto desempenho com backup/restauração do plano anterior.
- Diagnóstico ICMP local/externo, perda, latência, jitter simples e DNS.
- Limpeza seletiva de Temp e caches de Brave/Discord, com proteção contra reparse points.
- Toolbox pesquisável e leitura WMI de estado/inicialização de serviços protegidos.
- Perfil GIGABYTE A520M K V2 com revisões 1.0/1.1/1.2, versões de BIOS e datas verificadas em 27/11/2026.
- Base GitHub de perfis, modelo para outras placas e políticas de preservação.
- Build autocontido win-x64 e script de instalação para C:\ynoez.

## Validação concluída
- GitHub Actions Windows Server 2025 / .NET 8: builds 0.1.0 e 0.1.1 concluídos com sucesso (runs 46047088069 e 46047742654).
- Artefato 0.1.1: ZIP de 67.427.944 bytes, SHA-256 `34a52bdae9f8f65fc8ab2e673fa25db8ef734f69789efbfa1ba0a3f535fa1408`, contém `ynoez.exe` x64 para Windows GUI.
- Executável extraído: 161.822.880 bytes, PE32+ x86-64, subsystem Windows GUI.
- XML, JSON, handlers XAML e léxico C# verificados; ZIPs de entrega aprovados no teste de integridade.

## Próximas etapas
1. Testar abertura, navegação, inventário WMI e ações seguras em um Windows real.
2. Implementar modos Performance / Equilibrado / AFK com allowlist, processos protegidos e rollback.
3. Adicionar monitoramento ao vivo e registro persistente de quedas da rede.
4. Adicionar ações reversíveis à Toolbox somente após validar estado e rollback.
5. Expandir catálogo de placas por modelo e revisão com documentação oficial.
6. Adicionar ícone e instalador final.

## Limitações de validação
A compilação em CI não substitui teste de execução no PC do proprietário. O executável ainda não foi aberto numa sessão Windows interativa nem testado com Vanguard/FACEIT ou a placa física. O ping mostra amostras; não prova a causa de desconexões intermitentes nem evita quedas do roteador/provedor. Nenhum ganho de FPS foi medido.