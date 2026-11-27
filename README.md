# ynoez — Windows tuning, debloat seletivo e perfis de hardware

Aplicativo nativo para Windows em C# / WPF (.NET 8). A linguagem visual toma como referência o Stix Free Utility (sidebar, cartões escuros, Poppins e destaque rosa), com implementação própria do ynoez.

## Aplicativo

- Projeto: `src/ynoez/`
- Compilação Windows: `build.bat` ou `scripts/publish.ps1`
- Instalação local: `scripts/install.ps1`
- Publicação automática: `.github/workflows/build.yml` (artefato win-x64)
- Instalação: `C:\ynoez\`, com `Toolbox\`, `Data\`, `Backups\`, `Logs\` e `Downloads\` na mesma raiz.

## Estado do aplicativo — 0.2.2 (em desenvolvimento)

- **Home:** inventário de CPU, GPU, RAM, Windows, placa-mãe, BIOS e adaptadores ativos.
- **Monitor:** CPU/RAM em tempo real; GPU 3D via NVIDIA SMI ou contadores do Windows; temperatura/VRAM/potência quando o driver NVIDIA oferece esses dados.
- **Benchmark:** PresentMon 2.6.0 oficial, baixado somente quando solicitado e validado por SHA-256. Captura de 30–180 segundos, FPS médio, 1% low calculado pelo inverso do P99 de frametime, frametime médio e contagem de frames lentos. CSV salvo em `Logs\`. A captura pode solicitar UAC.
- **Tweaks:** perfis Performance/Equilibrado/AFK, detecção automática de jogos e inatividade, backup/restauração do plano original e catálogo de 11 ajustes HKCU com detecção, confirmação, verificação e reversão.
- **Network:** diagnóstico ICMP, TCP/443, DNS IPv4/IPv6 e tracert; monitoramento contínuo de gateway/1.1.1.1 com janela móvel de perda/jitter, TCP/443 e CSV; consulta opcional de energia de adaptadores físicos e ajuste reversível de Selective Suspend/Device Sleep quando suportado.
- **Debloat:** limpeza seletiva de Temp e caches Brave/Discord, preservando perfis e credenciais; gerenciador de inicialização com desativação reversível de entradas HKCU; catálogo limitado de serviços opcionais com backup de modo de inicialização e restauração.
- **Motherboard:** identificação da placa e revisão, perfil A520M K V2 com fontes oficiais por revisão.
- **Toolbox:** pesquisa de recursos/configurações Windows e consulta de serviços protegidos.
- **Backup, Restore e Logs:** snapshots locais, ponto de restauração opcional e histórico.

## Segurança e limites

O ynoez não remove nem desativa Microsoft Store/licenciamento, impressão, Calculadora, Clipboard, Bloco de Notas, Windows Search, Windows Update, Defender, Firewall, SmartScreen, UAC, drivers essenciais, NVIDIA Instant Replay/Alt+Z, Vanguard ou FACEIT. Preserva TPM, Secure Boot, VBS/HVCI e IOMMU/DMA.

O catálogo de serviços opcionais é restrito a DiagTrack, dmwappushservice, MapsBroker, Fax, RetailDemo e WMPNetworkSvc; não há desligamento em massa. Cada operação exige confirmação, e alterações administrativas solicitam UAC. A opção de energia de NIC altera somente recursos compatíveis e não reinicia o adaptador automaticamente.

O benchmark PresentMon não instala driver de sensor. O executável oficial é baixado sob demanda de [GameTechDev/PresentMon](https://github.com/GameTechDev/PresentMon/releases/tag/v2.6.0), validado pelo hash SHA-256 fixado no código e acompanhado dos avisos MIT/terceiros. A medição exige permissão ETW e pode solicitar UAC.

O projeto não automatiza flash de BIOS, overclock, tensão, PBO, BCD/HPET/timer tweaks ou desligamento em massa de serviços. Nenhuma ferramenta consegue garantir FPS, estabilidade de Wi-Fi ou impedir quedas causadas pelo roteador/ISP.

## Build e testes

O GitHub Actions compila em Windows x64, executa testes de integração de Registro em HKCU isolado, valida o parser de CSV do PresentMon e verifica a whitelist de serviços protegidos antes de publicar o executável autocontido.

A CI não substitui teste de execução no PC de destino. Ainda é necessário validar abertura, navegação, WMI, monitoramento prolongado, PresentMon com jogos reais, energia de NIC, hardware físico e compatibilidade com Vanguard/FACEIT numa máquina real.

## Banco de perfis

- [Perfil GIGABYTE A520M K V2](Profiles/Motherboards/GIGABYTE/A520M-K-V2.json)
- [Política segura da A520M K V2](Configs/Motherboards/GIGABYTE/A520M-K-V2-safe.json)
- [Whitelist de componentes Windows preservados](Configs/Debloat/Windows-preserve.json)
- [Guia para cadastrar novas placas](Profiles/Motherboards/README.md)

A revisão física impressa no PCB deve ser confirmada antes de escolher BIOS; o valor SMBIOS não é garantia suficiente.

## Construir localmente

Requer .NET 8 SDK no Windows:

```powershell
.\build.bat
# ou
pwsh -ExecutionPolicy Bypass -File .\scripts\publish.ps1
```

O executável autocontido será gerado em `dist\ynoez.exe`. Para instalar em `C:\ynoez`, execute `scripts\install.ps1` como administrador.

## Próximas etapas

Ainda faltam gerenciamento de AppX/recursos/tarefas com restauração comprovada, comparação automática de benchmarks antes/depois, 0,1% lows, arquitetura MVVM/DI, cobertura maior de testes de falha, instalador/atualizador/desinstalador e validação física de compatibilidade. A telemetria de temperatura de CPU e sensores AMD/Intel depende de APIs específicas ainda não integradas.

## Licença

Núcleo ynoez: MIT. PresentMon é distribuído sob MIT e inclui avisos de terceiros em `Toolbox\Performance\`. Marcas, nomes e links de terceiros pertencem aos respectivos fabricantes.
