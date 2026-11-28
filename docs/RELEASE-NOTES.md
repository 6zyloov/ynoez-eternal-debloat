# ynoez 0.3.0 — candidato integrado

Este artefato é uma build candidata para avaliação, não uma release estável.

## Incluído

- Aplicativo WPF .NET 10 LTS, self-contained para Windows x64.
- Perfis Performance/Equilibrado/AFK; detecção de jogos e proteção de scripts/automação comuns.
- Catálogo embarcado de processos validado por schema; ausente ou inválido bloqueia economia AFK agressiva.
- Inventário somente leitura de processos em execução para cadastrar nomes adicionais na lista AFK, sem ler argumentos/caminhos nem alterar processos.
- Monitor CPU/RAM/GPU com agregação WMI de engine 3D; monitor e diagnóstico IPv4/IPv6.
- Benchmark PresentMon com verificação SHA-256, relatórios CSV/JSON.
- Ferramentas de inventário Windows, manutenção, backups/restauração e perfil GIGABYTE A520M K V2.
- Setup Inno e pacote portátil, hashes SHA-256 e verificação de instalação/reinstalação/desinstalação no runner.

## Integridade e validação

A CI desta build executou auditoria NuGet, testes xUnit, publish Windows, instalador Inno, instalação limpa, reinstalação sobre a instalação, desinstalação preservando Data/Backups/Logs/Downloads, dry-run de release e hashes. O commit e run exatos estão registrados no PR #12 e em GitHub Actions.

Compare o arquivo SHA256SUMS.txt publicado com os hashes calculados localmente antes de executar os pacotes.

## Limites

Esta CI roda em Windows Server 2025. Ela não substitui validação em Windows 11 cliente ou hardware real. Roblox + automações XT, Vanguard/FACEIT, telemetria de CPU/AMD/Intel, recuperação após falha e migração/rollback de versões anteriores ainda precisam de testes dedicados. A assinatura Authenticode depende de certificado válido. Nenhum ganho de FPS ou estabilidade de rede é prometido.
