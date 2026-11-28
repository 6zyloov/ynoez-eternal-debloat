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
- Setup Inno e pacote portátil, hashes SHA-256 e smoke tests de instalação/reinstalação/desinstalação.
- SBOM SPDX 2.2 separada e manifesto SHA256SUMS; status Authenticode explícito (assinatura real opcional quando houver certificado Code Signing).
- CI de upgrade do baseline atual de main para o candidato, rollback, re-upgrade e verificação de retenção de dados.

## Integridade e validação

O GitHub Actions executa auditoria NuGet, testes xUnit, publicação Windows, instalador Inno, geração e validação de SBOM, smoke test de instalação, upgrade/rollback e dry-run de checksums. A execução desta rodada deve terminar com sucesso antes de considerar a entrega validada. O manifesto SHA256SUMS inclui os artefatos publicados relevantes.

Compare o arquivo SHA256SUMS.txt com os hashes calculados localmente antes de executar os pacotes. Consulte docs/SBOM-AND-SIGNING.md para os detalhes da SBOM e da assinatura.

## Limites

Esta CI roda em Windows Server 2025. Ela não substitui validação em Windows 11 cliente ou hardware real. Roblox + automações XT, Vanguard/FACEIT, telemetria de CPU/AMD/Intel, recuperação após falha e migração entre releases públicas ainda precisam de testes dedicados. O teste automatizado de migração usa o baseline atual de main, não uma release estável publicada. Authenticode só fica assinado quando um certificado válido está configurado. Nenhum ganho de FPS ou estabilidade de rede é prometido.
