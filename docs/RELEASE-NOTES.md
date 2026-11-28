# ynoez 0.3.0 — candidato integrado

Este artefato é uma build candidata para avaliação, não uma release estável.

## Interface e correções desta revisão

- Identidade visual monocromática integral: fundo preto absoluto, painéis quase pretos, texto branco de alto contraste e detalhes brancos. Sem destaque rosa.
- Temas globais aplicados a seletores, listas, campos, menus, botões, checkboxes, radiobuttons e tabelas; itens selecionados continuam escuros e legíveis.
- Corrigido o crash WPF de navegação em Tweaks causado pela inserção duplicada do mesmo controle visual.
- Detecção automática de CS2 em execução/bibliotecas Steam e do Roblox mais recente instalado; seleção manual continua como alternativa.

## Interface e operações de otimização

- Home simplificada, tema monocromático preto/branco e rótulos de botões legíveis em controles de alto contraste.
- Painel de Tweaks com ação de otimização para jogos: captura Game DVR do Windows desativada, preferência de efeitos visuais para desempenho, animações da barra de tarefas e transparência desativadas; aplicação com diário e restauração. Alto desempenho é aplicado somente quando não há outro perfil de energia ynoez gerenciando um backup.
- Debloat consulta startup, serviços e tarefas em segundo plano para a interface continuar responsiva.
- Remoção AppX restrita a allowlist exata de aplicativos opcionais do usuário atual. Não remove frameworks, componentes protegidos, provisionamento nem pacotes de outros usuários; a remoção não tem rollback automático.

## Incluído

- Aplicativo WPF .NET 10 LTS, self-contained para Windows x64.
- Perfis Performance/Equilibrado/AFK; detecção de jogos e proteção de scripts/automação comuns.
- **Foco de Jogo:** prioridade configurável de CPU para jogo principal e bots AFK (Alta/Acima do normal no foco; Idle/Abaixo do normal no fundo), prioridades de memória Normal/Very Low, preferências de adaptador gráfico do Windows e journal de restauração. Uma configuração de RobloxPlayerBeta.exe alcança todas as instâncias desse processo.
- Catálogo embarcado de processos validado por schema; ausente ou inválido bloqueia economia AFK agressiva.
- Inventário somente leitura de processos em execução para cadastrar nomes adicionais na lista AFK, sem ler argumentos/caminhos nem alterar processos.
- Monitor CPU/RAM/GPU com agregação WMI de engine 3D; monitor e diagnóstico IPv4/IPv6.
- Benchmark PresentMon com verificação SHA-256, relatórios CSV/JSON.
- Ferramentas de inventário Windows, manutenção, backups/restauração e perfil GIGABYTE A520M K V2.
- Setup Inno e pacote portátil, hashes SHA-256 e smoke tests de instalação/reinstalação/desinstalação.
- SBOM SPDX 2.2 separada e manifesto SHA256SUMS; status Authenticode explícito (assinatura real opcional quando houver certificado Code Signing).
- CI de upgrade do baseline atual de main para o candidato, rollback, re-upgrade e verificação de retenção de dados.

## Integridade e validação

A revisão de código `4388929e99a1308b5b9055486232c50fd5d5123e`, incluindo a correção da interface e o fluxo de detecção automática, passou pela CI no Windows Server 2025 ([run 46103835497](https://github.com/6zyloov/ynoez-eternal-debloat/actions/runs/46103835497)). Foram aprovados auditoria NuGet, testes xUnit, publicação self-contained, instalador, SBOM, instalação limpa, reinstalação, desinstalação preservando dados, upgrade do baseline atual de main, rollback, re-upgrade, checksums e upload dos dois artefatos. O manifesto inclui instalador, pacote portátil, SBOM e status de assinatura.

Na SBOM atual, seis componentes e dez arquivos foram identificados, mas os campos de licença dos pacotes permanecem NOASSERTION; a revisão formal de licenças ainda é necessária. A distribuição atual declara Authenticode NOT SIGNED porque nenhum certificado de assinatura foi configurado.

Compare SHA256SUMS.txt com os hashes calculados localmente antes de executar os pacotes. Consulte docs/SBOM-AND-SIGNING.md para os detalhes da SBOM e da assinatura.

## Limites

Esta CI roda em Windows Server 2025. Ela não substitui validação em Windows 11 cliente ou hardware real. Roblox + automações XT, Vanguard/FACEIT, telemetria de CPU/AMD/Intel, recuperação após falha e migração entre releases públicas ainda precisam de testes dedicados. O teste automatizado de migração usa o baseline atual de main, não uma release estável publicada. Authenticode só fica assinado quando um certificado válido está configurado. A prioridade de CPU High pode consumir quase todo o tempo disponível; Realtime não é usado. Prioridade de memória não reserva RAM, e preferência gráfica não reserva GPU/VRAM nem garante uma fatia de processamento. Nenhum ganho de FPS ou estabilidade de rede é prometido.
