# ynoez — Windows tuning, debloat seletivo e perfis de hardware

Aplicativo nativo para Windows em C# / WPF (.NET 8), com uma base independente para perfis de placas-mãe e políticas de preservação. A linguagem visual do painel toma como referência a estrutura do Stix Free Utility (sidebar, cartões escuros, Poppins e destaque rosa), mas a implementação do ynoez é própria.

## Aplicativo

- Projeto: `src/ynoez/`
- Compilação Windows: `build.bat` ou `scripts/publish.ps1`
- Instalação local: `scripts/install.ps1`
- Publicação automática: `.github/workflows/build.yml` (artefato win-x64)

O instalador copia o aplicativo para `C:\ynoez\`, criando `Toolbox\`, `Data\`, `Backups\`, `Logs\` e `Downloads\) dentro da mesma raiz.

## Estado do aplicativo — 0.1.0

- Home: inventário de CPU, GPU, RAM, Windows, placa-mãe, BIOS e adaptadores ativos.
- Backup: snapshot local de hardware, plano de energia e configuração de rede; ponto de restauração opcional.
- Tweaks: plano Alto desempenho com backup/restauração do plano anterior e atalhos oficiais.
- Network: ping do gateway e de 1.1.1.1, perda, latência, jitter simples e resolução DNS.
- Debloat: limpeza seletiva de Temp e caches do Brave/Discord, mantendo perfis e credenciais.
- Motherboard: detecção da placa e revisão, perfil por modelo/revisão e links oficiais.
- Toolbox: busca de ferramentas/configurações Windows, com consulta somente leitura de serviços protegidos.
- Restore e Logs: acesso à restauração, snapshots locais e logs.

Esta é uma primeira fundação. Monitoramento contínuo, perfis automáticos Performance/Equilibrado/AFK, mais ações reversíveis e catálogo amplo de placas ainda não estão concluídos. O código não foi declarado pronto para produção.

## Regras de segurança

Não remover/desativar Microsoft Store/licenciamento, impressão, Calculadora, Clipboard, Bloco de Notas, Windows Search, Windows Update, Defender, Firewall, SmartScreen, UAC, drivers essenciais, NVIDIA Instant Replay/Alt+Z, Vanguard ou FACEIT. Preservar TPM, Secure Boot, VBS/HVCI, IOMMU/DMA e recursos necessários aos anti-cheats.

O projeto não automatiza flash de BIOS, overclock, tensão, PBO, BCD/HPET/timer tweaks ou desligamento em massa de serviços. Cada ação futura precisa de compatibilidade confirmada, backup, consentimento, log e reversão.

## Banco de perfis

O mesmo repositório mantém dados separados da aplicação:

- [Perfil GIGABYTE A520M K V2](Profiles/Motherboards/GIGABYTE/A520M-K-V2.json)
- [Política segura da A520M K V2](Configs/Motherboards/GIGABYTE/A520M-K-V2-safe.json)
- [Whitelist de componentes Windows preservados](Configs/Debloat/Windows-preserve.json)
- [Guia para cadastrar novas placas](Profiles/Motherboards/README.md)

O perfil da A520M K V2 separa revisões 1.0, 1.1 e 1.2. A revisão física impressa no PCB deve ser confirmada antes de escolher BIOS; o valor SMBIOS não é garantia suficiente.

## Construir localmente

Requer .NET 8 SDK no Windows:

```powershell
.\build.bat
# ou
pwsh -ExecutionPolicy Bypass -File .\scripts\publish.ps1
```

O executável autocontido deve aparecer em `dist\ynoez.exe`. Para instalar em `C:\ynoez`, execute `scripts\install.ps1` como administrador.

## Limitações da primeira versão

O workflow valida a compilação; não valida estabilidade em hardware real nem ganhos de FPS. A primeira versão ainda precisa de build real e testes em Windows 10/11. Diagnósticos de ping representam amostras e não garantem a identificação de quedas intermitentes do provedor/roteador.

## Licença

MIT. Marcas, nomes e links de terceiros pertencem aos respectivos fabricantes.
