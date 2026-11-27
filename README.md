# ynoez — Windows tuning, debloat seletivo e perfis de hardware

Aplicativo nativo para Windows em C# / WPF (.NET 8). A linguagem visual toma como referência o Stix Free Utility (sidebar, cartões escuros, Poppins e destaque rosa), com implementação própria do ynoez.

## Aplicativo

- Projeto: `src/ynoez/`
- Compilação Windows: `build.bat` ou `scripts/publish.ps1`
- Instalação local: `scripts/install.ps1`
- Publicação automática: `.github/workflows/build.yml` (artefato win-x64)
- Instalação proposta: `C:\ynoez\`, com `Toolbox\`, `Data\`, `Backups\`, `Logs\` e `Downloads\` na mesma raiz.

## Estado do aplicativo — 0.1.1

- Home: inventário de CPU, GPU, RAM, Windows, placa-mãe, BIOS e adaptadores ativos.
- Backup: snapshot local de hardware, plano de energia e configuração de rede; ponto de restauração opcional.
- Tweaks: plano Alto desempenho com backup/restauração do plano anterior e atalhos oficiais.
- Network: ping do gateway e de 1.1.1.1, perda, latência, jitter simples e resolução DNS.
- Debloat: limpeza seletiva de Temp e caches do Brave/Discord, preservando perfis e credenciais.
- Motherboard: identificação da placa e revisão, perfil A520M K V2 com a BIOS mais recente listada para cada revisão e links oficiais (consulta em 27/11/2026).
- Toolbox: pesquisa de recursos/configurações Windows e consulta somente leitura de serviços protegidos.
- Restore e Logs: acesso à restauração, snapshots locais e logs.

## Build verificado

A versão 0.1.1 foi compilada com sucesso pelo GitHub Actions em Windows Server 2025 / .NET 8. O artefato `ynoez-win-x64` foi publicado no [workflow 46047742654](https://github.com/6zyloov/ynoez-eternal-debloat/actions/runs/46047742654). O executável é autocontido para Windows x64.

A compilação de CI não é um teste de execução no PC de destino. Ainda é necessário validar abertura, navegação, WMI, operações seguras, Vanguard/FACEIT e a placa física num Windows real.

## Regras de segurança

Não remover/desativar Microsoft Store/licenciamento, impressão, Calculadora, Clipboard, Bloco de Notas, Windows Search, Windows Update, Defender, Firewall, SmartScreen, UAC, drivers essenciais, NVIDIA Instant Replay/Alt+Z, Vanguard ou FACEIT. Preservar TPM, Secure Boot, VBS/HVCI e IOMMU/DMA.

O projeto não automatiza flash de BIOS, overclock, tensão, PBO, BCD/HPET/timer tweaks ou desligamento em massa de serviços. Cada ação futura precisa de compatibilidade confirmada, backup, consentimento, log e reversão.

## Banco de perfis

- [Perfil GIGABYTE A520M K V2](Profiles/Motherboards/GIGABYTE/A520M-K-V2.json)
- [Política segura da A520M K V2](Configs/Motherboards/GIGABYTE/A520M-K-V2-safe.json)
- [Whitelist de componentes Windows preservados](Configs/Debloat/Windows-preserve.json)
- [Guia para cadastrar novas placas](Profiles/Motherboards/README.md)

O perfil separa revisões 1.0, 1.1 e 1.2. A revisão física impressa no PCB deve ser confirmada antes de escolher BIOS; o valor SMBIOS não é garantia suficiente.

## Construir localmente

Requer .NET 8 SDK no Windows:

```powershell
.\build.bat
# ou
pwsh -ExecutionPolicy Bypass -File .\scripts\publish.ps1
```

O executável autocontido será gerado em `dist\ynoez.exe`. Para instalar em `C:\ynoez`, execute `scripts\install.ps1` como administrador.

## Próximas etapas

Monitoramento contínuo de CPU/GPU/temperaturas, modos Performance/Equilibrado/AFK, rastreamento persistente de quedas de rede, ajustes reversíveis individuais da Toolbox, mais perfis de placas e instalador final ainda precisam ser desenvolvidos/testados.

## Licença

MIT. Marcas, nomes e links de terceiros pertencem aos respectivos fabricantes.