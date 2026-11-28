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

A CI do commit `3c07809e50fc56fb74767bafeeb1cabfac990d99` terminou com sucesso no Windows Server 2025 ([run 46092514834](https://github.com/6zyloov/ynoez-eternal-debloat/actions/runs/46092514834)). Foram aprovados auditoria NuGet, testes xUnit, publicação e instalador, geração/validação de SBOM, instalação limpa, reinstalação, desinstalação preservando dados, upgrade do baseline atual de main, rollback, re-upgrade e dry-run de SHA-256. O manifesto inclui instalador, pacote portátil, SBOM e status de assinatura.

Na SBOM atual, seis componentes e dez arquivos foram identificados, mas os campos de licença dos pacotes permanecem NOASSERTION; a revisão formal de licenças ainda é necessária. A distribuição atual declara Authenticode NOT SIGNED porque nenhum certificado de assinatura foi configurado.

Compare SHA256SUMS.txt com os hashes calculados localmente antes de executar os pacotes. Consulte docs/SBOM-AND-SIGNING.md para os detalhes da SBOM e da assinatura.

## Limites

Esta CI roda em Windows Server 2025. Ela não substitui validação em Windows 11 cliente ou hardware real. Roblox + automações XT, Vanguard/FACEIT, telemetria de CPU/AMD/Intel, recuperação após falha e migração entre releases públicas ainda precisam de testes dedicados. O teste automatizado de migração usa o baseline atual de main, não uma release estável publicada. Authenticode só fica assinado quando um certificado válido está configurado. Nenhum ganho de FPS ou estabilidade de rede é prometido.
