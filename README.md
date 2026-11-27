# ynoez — Windows tuning, debloat seletivo e perfis de hardware

Aplicativo nativo para Windows em C# / WPF (.NET 8). A linguagem visual toma como referência o Stix Free Utility (sidebar, cartões escuros, Poppins e destaque rosa), com implementação própria do ynoez.

## Aplicativo

- Projeto: `src/ynoez/`
- Compilação Windows: `build.bat` ou `scripts/publish.ps1`
- Instalação local: `scripts/install.ps1`
- Publicação automática: `.github/workflows/build.yml` (artefato win-x64)
- Instalação proposta: `C:\ynoez\`, com `Toolbox\`, `Data\`, `Backups\`, `Logs\` e `Downloads\` na mesma raiz.

## Estado do aplicativo — 0.2.0 (em desenvolvimento)

- Home: inventário de CPU, GPU, RAM, Windows, placa-mãe, BIOS e adaptadores ativos.
- Backup: snapshot local de hardware, plano de energia e configuração de rede; ponto de restauração opcional.
- Tweaks: plano Alto desempenho com backup/restauração e catálogo integrado de ajustes HKCU, com estado detectado, confirmação, backup por valor, verificação e reversão.
- Network: diagnóstico ICMP/DNS, cancelamento correto e monitoramento contínuo de gateway/1.1.1.1 com histórico CSV.
- Debloat: limpeza seletiva de Temp e caches do Brave/Discord, preservando perfis e credenciais; gerenciador de inicialização com desativação reversível de entradas HKCU e proteção de itens sensíveis.
- Motherboard: identificação da placa e revisão, perfil A520M K V2 com a BIOS mais recente listada para cada revisão e links oficiais (consulta em 27/11/2026).
- Toolbox: pesquisa de recursos/configurações Windows e consulta somente leitura de serviços protegidos.
- Restore e Logs: acesso à restauração, snapshots locais e logs.

## Build e testes

O GitHub Actions compila em Windows x64, executa testes de integração de Registro em HKCU isolado e publica o executável autocontido como artefato. Consulte a aba Actions para a execução associada ao commit mais recente.

A CI não substitui teste de execução no PC de destino. Ainda é necessário validar abertura, navegação, WMI, monitoramento prolongado, hardware físico e compatibilidade com Vanguard/FACEIT numa máquina real.

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

Ainda faltam modos Performance/Equilibrado/AFK com allowlist, monitoramento de sensores CPU/GPU, medição de FPS/frametime, inventário e gerenciamento seguro de AppX/recursos/tarefas/serviços, cobertura maior de testes, instalador/atualizador e validação física de compatibilidade. O monitor de rede registra ICMP, mas não identifica sozinho a causa de cada queda.

## Licença

MIT. Marcas, nomes e links de terceiros pertencem aos respectivos fabricantes.