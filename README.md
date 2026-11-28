# ynoez — Windows tuning, debloat seletivo e perfis de hardware

Aplicativo nativo para Windows em C# / WPF (.NET 10 LTS). A linguagem visual toma como referência o Stix Free Utility (sidebar, cartões escuros, Poppins e destaque rosa), com implementação própria do ynoez.

## Sistemas operacionais e ciclo de suporte

O aplicativo é x64 e usa .NET 10 LTS. Conforme a matriz oficial do .NET 10 para Windows, Windows 11 deve estar em uma versão atualmente suportada; Windows 10 aparece como suportado apenas nas edições Enterprise/LTSC listadas pela Microsoft. Windows 10 Home/Pro não faz parte da matriz de suporte do .NET 10, e o suporte geral dessas edições terminou em 14 de outubro de 2025. Não prometemos compatibilidade oficial nessas edições.

O .NET 10 é LTS e, na política publicada em 10 de novembro de 2026, está previsto para permanecer em suporte até 14 de novembro de 2028. A CI do projeto roda em Windows Server 2025; isso valida o ambiente de build/teste, mas não substitui testes em Windows 11 cliente nem em hardware físico.

## Aplicativo

- Projeto: src/ynoez/
- Compilação Windows: build.bat ou scripts/publish.ps1
- Instalação local: scripts/install.ps1
- Publicação de builds: .github/workflows/build.yml (artefatos win-x64); releases estáveis por tag vX.Y.Z em .github/workflows/release.yml
- Instalação: C:\ynoez\, com Toolbox\, Data\, Backups\, Logs\ e Downloads\ na mesma raiz.

## Estado do aplicativo — 0.3.0 (candidato integrado)

- **Home:** inventário de CPU, GPU, RAM total, Windows, placa-mãe e BIOS; consulta sob demanda dos módulos RAM reportados por WMI/SMBIOS (posição, capacidade, DDR, velocidades, formato, ECC e part number), sem afirmar single/dual channel nem ler SPD diretamente. Mantém auditoria somente leitura de Defender, Firewall, TPM, Secure Boot, VBS/HVCI e Search/impressão/Store/Update/Vanguard/FACEIT; verificação manual de atualização estável sem download automático.
- **Monitor:** CPU/RAM em tempo real; GPU 3D via NVIDIA SMI ou contadores do Windows; temperatura/VRAM/potência quando o driver NVIDIA oferece esses dados.
- **Benchmark:** PresentMon 2.6.0 oficial, baixado somente quando solicitado e validado por SHA-256. Captura de 30–180 segundos, FPS médio, 1% low calculado pelo inverso do P99 de frametime, frametime médio e contagem de frames lentos. CSV salvo em Logs\. A captura pode solicitar UAC.
- **Tweaks:** perfis Performance/Equilibrado/AFK e AFK protegido. Jogos conhecidos e runtimes comuns de scripts/automação (cmd, PowerShell, Python, Node.js, Java, WSH e AutoHotkey) ficam protegidos por padrão; a lista personalizada é somada aos padrões e não os apaga. Um seletor consulta processos em execução em modo somente leitura (nome, PID e memória) para ajudar a cadastrar o processo XT correto sem ler argumentos/caminhos. O catálogo embarcado exige schema válido e listas não vazias; configuração ausente/corrompida falha com segurança para AFK protegido. Nenhum processo é encerrado; sessão de energia exportada/restaurada; 11 ajustes HKCU reversíveis.
- **Execução em segundo plano:** fechar a janela com uma operação longa ativa oculta o ynoez na bandeja do sistema em vez de encerrar o monitor de rede, o perfil automático, o benchmark ou a manutenção do Windows; duplo clique no ícone reabre a janela e o menu da bandeja permite encerrar explicitamente.
- **Foco de Jogo:** página para escolher um executável principal e vários executáveis AFK. O alvo principal usa prioridade de CPU Alta (padrão com confirmação) ou Acima do normal, prioridade de memória Normal e preferência de adaptador gráfico de alto desempenho do Windows. Os alvos AFK usam CPU Idle (extremamente baixa) ou Abaixo do normal, prioridade de memória Very Low e preferência gráfica de economia. A regra é aplicada a todas as instâncias com o mesmo nome de executável, então um único cadastro de RobloxPlayerBeta.exe abrange os dois clientes e instâncias futuras. O ynoez reaplica o perfil a cada dois segundos, permanece na bandeja enquanto o perfil estiver ativo e registra os estados originais para restauração/recovery. O perfil salvo exige ativação explícita em cada execução do ynoez; ao sair normalmente, o programa restaura as prioridades e não o reativa automaticamente na próxima abertura.
- **Network:** inventário somente leitura de interfaces, drivers, endereços, gateways, DNS e MTU; diagnóstico cancelável ICMP/TCP/DNS IPv4/IPv6/tracert; monitoramento contínuo a cada 5 segundos de gateways IPv4/IPv6, Cloudflare e Google por ICMP/TCP/443 nas duas famílias, DNS com contagem A/AAAA e janela móvel de 60 segundos com perda/jitter separados por família/protocolo. Distingue ausência/filtragem IPv6, rede local, DNS, ICMP e TCP; exporta CSV completo. Contadores RX/TX e energia de NIC reversível.
- **Debloat:** limpeza seletiva de Temp e caches Brave/Discord, preservando perfis e credenciais; inventários de programas Win32 (HKLM/HKCU 32/64 bits) e AppX/MSIX somente leitura para usuário atual, todos os perfis e pacotes provisionados, com busca e exportação CSV; gerenciador de inicialização HKCU + arquivos da pasta pessoal com backup e reversão (HKLM e pasta Startup compartilhada somente leitura); whitelist de seis serviços opcionais e seis tarefas agendadas opcionais com backup, verificação e restauração.
- **Motherboard:** perfil GIGABYTE A520M K V2; revisão SMBIOS aparece só como indício e links de BIOS específicos exigem confirmação manual da serigrafia no PCB. Inventário sob demanda de dispositivos PnP de rede/áudio/sistema com hardware IDs e metadados do driver assinado (provedor, versão, data e INF), sem instalação automática.
- **Toolbox:** pesquisa de configurações e atalhos Windows; inventário sob demanda somente leitura de serviços Win32 e tarefas agendadas com busca, classificação de componentes protegidos/whitelist opcional, estado, horários/descrições e CSV. Tarefas desconhecidas e serviços não avaliados permanecem somente leitura; alterações continuam nas whitelists reversíveis de Debloat.
- **Reparo do Windows:** DISM CheckHealth/ScanHealth/RestoreHealth e SFC VerifyOnly/Scannow em comandos allowlisted; verificações separadas de reparos, confirmação explícita, UAC, timeout/cancelamento, logs individuais e sem reinicialização automática.
- **Backup, Restore e Logs:** central de recuperação para Registro, plano base, sessão AFK interrompida, serviços, adaptadores, tarefas e inicialização; snapshots locais com nomes únicos, manifesto SHA-256 e descarte de snapshots incompletos; ponto de restauração opcional.
- **Interface:** Poppins incorporada ao executável (Regular, Medium, SemiBold e Bold) com licença SIL Open Font License.

## Instalação, SBOM e integridade

A versão do app e do instalador vem de um único campo Version em src/ynoez/ynoez.csproj. O builder gera um script Inno Setup temporário com valores literais e verifica os metadados PE antes do upload.

O pipeline gera e valida uma SBOM SPDX 2.2, publica o manifesto e inclui seu SHA-256 junto ao instalador e pacote portátil. Na SBOM desta build, os pacotes foram identificados, mas os campos de licença estão como NOASSERTION; a revisão formal de licenças continua pendente. O ciclo de assinatura aceita certificado PFX em secrets do GitHub Actions: o executável é assinado antes de ser empacotado e o instalador é assinado depois. Sem certificado, a distribuição declara explicitamente Authenticode NOT SIGNED; não simula uma assinatura. Consulte docs/SBOM-AND-SIGNING.md.

A CI do commit `3c07809e50fc56fb74767bafeeb1cabfac990d99` passou no GitHub Actions em Windows Server 2025 ([run 46092514834](https://github.com/6zyloov/ynoez-eternal-debloat/actions/runs/46092514834)). Foram aprovados instalação limpa, reinstalação, desinstalação preservando Data/Backups/Logs/Downloads, upgrade do baseline atual de main para o candidato, rollback, re-upgrade, validação da SBOM e dos hashes. Esse teste de baseline não substitui a migração de uma futura versão estável já publicada.

## Segurança e limites

O ynoez não remove nem desativa Microsoft Store/licenciamento, impressão, Calculadora, Clipboard, Bloco de Notas, Windows Search, Windows Update, Defender, Firewall, SmartScreen, UAC, drivers essenciais, NVIDIA Instant Replay/Alt+Z, Vanguard ou FACEIT. Preserva TPM, Secure Boot, VBS/HVCI e IOMMU/DMA.

O catálogo de serviços opcionais é restrito a DiagTrack, dmwappushservice, MapsBroker, Fax, RetailDemo e WMPNetworkSvc; não há desligamento em massa. Cada operação exige confirmação, e alterações administrativas solicitam UAC. A opção de energia de NIC altera somente recursos compatíveis e não reinicia o adaptador automaticamente.

O benchmark PresentMon não instala driver de sensor. O executável oficial é baixado sob demanda de GameTechDev/PresentMon v2.6.0, validado pelo hash SHA-256 fixado no código e acompanhado dos avisos MIT/terceiros. A medição exige permissão ETW e pode solicitar UAC.

O projeto não automatiza flash de BIOS, overclock, tensão, PBO, BCD/HPET/timer tweaks ou desligamento em massa de serviços. O Foco de Jogo não reserva RAM/VRAM nem divide uma porcentagem fixa da GPU: prioridade de memória é apenas uma dica ao gerenciador de memória para decidir quais páginas remover primeiro, e a preferência gráfica seleciona adaptador de alto desempenho/economia quando o hardware oferece essa escolha. A prioridade Alta de CPU pode consumir quase todo o tempo de processador disponível; use com confirmação e prefira Acima do normal se houver falhas de áudio ou perda de responsividade. Realtime nunca é usado. Nenhuma ferramenta consegue garantir FPS, estabilidade de Wi-Fi ou impedir quedas causadas pelo roteador/ISP.

## Build e testes

O GitHub Actions compila em Windows x64, audita dependências NuGet diretas e transitivas, executa os testes xUnit, publica o executável autocontido, constrói o instalador e gera a SBOM. A pipeline também faz o smoke test de instalação/reinstalação/desinstalação, testa upgrade e rollback entre o baseline de main e o candidato, valida integridade, e publica os artefatos de build e metadata. A CI desta revisão deve terminar antes de considerar essa rodada validada.

Directory.Build.props habilita a auditoria de vulnerabilidades NuGet e transforma alertas moderados ou superiores em erro de restore. Detalhes em docs/DEPENDENCY-AUDIT.md e docs/SBOM-AND-SIGNING.md.

A CI não substitui teste de execução no PC de destino. Ainda é necessário validar abertura, navegação, WMI, monitoramento prolongado, PresentMon com jogos reais, energia de NIC, hardware físico, sessões prolongadas com Roblox + XT e compatibilidade com Vanguard/FACEIT numa máquina real.

## Banco de perfis

- Perfil GIGABYTE A520M K V2: Profiles/Motherboards/GIGABYTE/A520M-K-V2.json
- Política segura da A520M K V2: Configs/Motherboards/GIGABYTE/A520M-K-V2-safe.json
- Whitelist de componentes Windows preservados: Configs/Debloat/Windows-preserve.json
- Guia para cadastrar novas placas: Profiles/Motherboards/README.md

A revisão física impressa no PCB deve ser confirmada antes de escolher BIOS; o valor SMBIOS não é garantia suficiente.

## Construir localmente

Requer .NET 10 SDK LTS no Windows. O repositório fixa a faixa do SDK em global.json para evitar que uma versão maior instalada localmente seja selecionada por engano:

```powershell
.\build.bat
# ou
pwsh -ExecutionPolicy Bypass -File .\scripts\publish.ps1
```

O executável autocontido será gerado em dist\ynoez.exe. Para instalar em C:\ynoez, execute scripts/install.ps1 como administrador.

## Próximas etapas

Ainda faltam validação em Windows cliente e hardware real, testes prolongados de Roblox + XT, PresentMon em jogos, sensores adicionais por APIs documentadas, compatibilidade autorizada com Vanguard/FACEIT e teste de recuperação após falhas. A assinatura efetiva Authenticode exige um certificado Code Signing válido configurado nos secrets do repositório. A primeira migração entre releases publicadas só poderá ser comprovada depois de existir uma versão anterior estável. A remoção AppX continua indisponível enquanto não houver recuperação e rollback confiáveis. A telemetria de temperatura de CPU e sensores AMD/Intel depende de APIs específicas ainda não integradas.

## Licença

Núcleo ynoez: MIT. PresentMon é distribuído sob MIT e inclui avisos de terceiros em Toolbox/Performance/. Marcas, nomes e links de terceiros pertencem aos respectivos fabricantes.
