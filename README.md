# ynoez — Windows tuning, debloat seletivo e perfis de hardware

Aplicativo nativo para Windows em C# / WPF (.NET 10 LTS). A linguagem visual toma como referência o Stix Free Utility (sidebar, cartões escuros, Poppins e destaque rosa), com implementação própria do ynoez.

## Aplicativo

- Projeto: `src/ynoez/`
- Compilação Windows: `build.bat` ou `scripts/publish.ps1`
- Instalação local: `scripts/install.ps1`
- Publicação de builds: `.github/workflows/build.yml` (artefatos win-x64); releases estáveis por tag `vX.Y.Z` em `.github/workflows/release.yml`
- Instalação: `C:\ynoez\`, com `Toolbox\`, `Data\`, `Backups\`, `Logs\` e `Downloads\` na mesma raiz.

## Estado do aplicativo — 0.3.0 (em desenvolvimento)

- **Home:** inventário de CPU, GPU, RAM total, Windows, placa-mãe e BIOS; consulta sob demanda dos módulos RAM reportados por WMI/SMBIOS (posição, capacidade, DDR, velocidades, formato, ECC e part number), sem afirmar single/dual channel nem ler SPD diretamente. Mantém auditoria somente leitura de Defender, Firewall, TPM, Secure Boot, VBS/HVCI e Search/impressão/Store/Update/Vanguard/FACEIT; verificação manual de atualização estável sem download automático.
- **Monitor:** CPU/RAM em tempo real; GPU 3D via NVIDIA SMI ou contadores do Windows; temperatura/VRAM/potência quando o driver NVIDIA oferece esses dados.
- **Benchmark:** PresentMon 2.6.0 oficial, baixado somente quando solicitado e validado por SHA-256. Captura de 30–180 segundos, FPS médio, 1% low calculado pelo inverso do P99 de frametime, frametime médio e contagem de frames lentos. CSV salvo em `Logs\`. A captura pode solicitar UAC.
- **Tweaks:** perfis Performance/Equilibrado/AFK e AFK protegido. Jogos conhecidos e processos extras escolhidos pelo usuário usam Equilibrado + bloqueio de suspensão quando o usuário está AFK; a economia agressiva só entra sem cargas protegidas. A lista é salva em Data; se sua configuração estiver corrompida, o modo automático falha com segurança para AFK protegido. Nenhum processo é encerrado; sessão de energia exportada/restaurada; 11 ajustes HKCU reversíveis.
- **Execução em segundo plano:** fechar a janela com uma operação longa ativa oculta o ynoez na bandeja do sistema em vez de encerrar o monitor de rede, o perfil automático, o benchmark ou a manutenção do Windows; duplo clique no ícone reabre a janela e o menu da bandeja permite encerrar explicitamente.
- **Network:** inventário somente leitura de interfaces, drivers, endereços, gateways, DNS e MTU; diagnóstico cancelável ICMP/TCP/DNS IPv4/IPv6/tracert; monitoramento contínuo a cada 5 segundos de gateways IPv4/IPv6, Cloudflare e Google por ICMP/TCP/443 nas duas famílias, DNS com contagem A/AAAA e janela móvel de 60 segundos com perda/jitter separados por família/protocolo. Distingue ausência/filtragem IPv6, rede local, DNS, ICMP e TCP; exporta CSV completo. Contadores RX/TX e energia de NIC reversível.
- **Debloat:** limpeza seletiva de Temp e caches Brave/Discord, preservando perfis e credenciais; inventários de programas Win32 (HKLM/HKCU 32/64 bits) e AppX/MSIX somente leitura para usuário atual, todos os perfis e pacotes provisionados, com busca e exportação CSV; gerenciador de inicialização HKCU + arquivos da pasta pessoal com backup e reversão (HKLM e pasta Startup compartilhada somente leitura); whitelist de seis serviços opcionais e seis tarefas agendadas opcionais com backup, verificação e restauração.
- **Motherboard:** perfil GIGABYTE A520M K V2; revisão SMBIOS aparece só como indício e links de BIOS específicos exigem confirmação manual da serigrafia no PCB. Inventário sob demanda de dispositivos PnP de rede/áudio/sistema com hardware IDs e metadados do driver assinado (provedor, versão, data e INF), sem instalação automática.
- **Toolbox:** pesquisa de configurações e atalhos Windows; inventário sob demanda somente leitura de serviços Win32 e tarefas agendadas com busca, classificação de componentes protegidos/whitelist opcional, estado, horários/descrições e CSV. Tarefas desconhecidas e serviços não avaliados permanecem somente leitura; alterações continuam nas whitelists reversíveis de Debloat.
- **Reparo do Windows:** DISM CheckHealth/ScanHealth/RestoreHealth e SFC VerifyOnly/Scannow em comandos allowlisted; verificações separadas de reparos, confirmação explícita, UAC, timeout/cancelamento, logs individuais e sem reinicialização automática.
- **Backup, Restore e Logs:** central de recuperação para Registro, plano base, sessão AFK interrompida, serviços, adaptadores, tarefas e inicialização; snapshots locais com nomes únicos, manifesto SHA-256 e descarte de snapshots incompletos; ponto de restauração opcional.
- **Interface:** Poppins incorporada ao executável (Regular, Medium, SemiBold e Bold) com licença SIL Open Font License.



A versão do app e do instalador vem de um único campo `<Version>` em `src/ynoez/ynoez.csproj`. O builder gera um script Inno Setup temporário com valores literais e verifica os metadados PE antes do upload; releases estáveis ainda dependem de tag `vX.Y.Z` correspondente.

## Segurança e limites

O ynoez não remove nem desativa Microsoft Store/licenciamento, impressão, Calculadora, Clipboard, Bloco de Notas, Windows Search, Windows Update, Defender, Firewall, SmartScreen, UAC, drivers essenciais, NVIDIA Instant Replay/Alt+Z, Vanguard ou FACEIT. Preserva TPM, Secure Boot, VBS/HVCI e IOMMU/DMA.

O catálogo de serviços opcionais é restrito a DiagTrack, dmwappushservice, MapsBroker, Fax, RetailDemo e WMPNetworkSvc; não há desligamento em massa. Cada operação exige confirmação, e alterações administrativas solicitam UAC. A opção de energia de NIC altera somente recursos compatíveis e não reinicia o adaptador automaticamente.

O benchmark PresentMon não instala driver de sensor. O executável oficial é baixado sob demanda de [GameTechDev/PresentMon](https://github.com/GameTechDev/PresentMon/releases/tag/v2.6.0), validado pelo hash SHA-256 fixado no código e acompanhado dos avisos MIT/terceiros. A medição exige permissão ETW e pode solicitar UAC.

O projeto não automatiza flash de BIOS, overclock, tensão, PBO, BCD/HPET/timer tweaks ou desligamento em massa de serviços. Nenhuma ferramenta consegue garantir FPS, estabilidade de Wi-Fi ou impedir quedas causadas pelo roteador/ISP.

## Build e testes

O GitHub Actions compila em Windows x64, audita dependências NuGet diretas e transitivas, executa testes de Registro, perfis AFK/protegidos, inventários de serviços/tarefas, SMBIOS/RAM, revisão física de placa, PnP/drivers, monitor de rede IPv4/IPv6, agregação de carga GPU e whitelists. Também publica executável autocontido e instalador, valida metadados PE, dry-run de release, SHA-256 e artefatos.

`Directory.Build.props` habilita a auditoria de vulnerabilidades NuGet e transforma alertas moderados ou superiores em erro de restore. Detalhes e limites em [Auditoria de dependências](docs/DEPENDENCY-AUDIT.md).

A CI não substitui teste de execução no PC de destino. Ainda é necessário validar abertura, navegação, WMI, monitoramento prolongado, PresentMon com jogos reais, energia de NIC, hardware físico e compatibilidade com Vanguard/FACEIT numa máquina real.

## Banco de perfis

- [Perfil GIGABYTE A520M K V2](Profiles/Motherboards/GIGABYTE/A520M-K-V2.json)
- [Política segura da A520M K V2](Configs/Motherboards/GIGABYTE/A520M-K-V2-safe.json)
- [Whitelist de componentes Windows preservados](Configs/Debloat/Windows-preserve.json)
- [Guia para cadastrar novas placas](Profiles/Motherboards/README.md)

A revisão física impressa no PCB deve ser confirmada antes de escolher BIOS; o valor SMBIOS não é garantia suficiente.

## Construir localmente

Requer .NET 10 SDK LTS no Windows. O repositório fixa a faixa do SDK em `global.json` para evitar que uma versão maior instalada localmente seja selecionada por engano:

```powershell
.\build.bat
# ou
pwsh -ExecutionPolicy Bypass -File .\scripts\publish.ps1
```

O executável autocontido será gerado em `dist\ynoez.exe`. Para instalar em `C:\ynoez`, execute `scripts\install.ps1` como administrador.

## Próximas etapas

Ainda faltam remoção AppX/provisioned com recuperação confiável, arquitetura MVVM/DI, assinatura Authenticode e validação física de compatibilidade. A remoção AppX continua indisponível enquanto não houver rollback confiável. A telemetria de temperatura de CPU e sensores AMD/Intel depende de APIs específicas ainda não integradas.

## Licença

Núcleo ynoez: MIT. PresentMon é distribuído sob MIT e inclui avisos de terceiros em `Toolbox\Performance\`. Marcas, nomes e links de terceiros pertencem aos respectivos fabricantes.
