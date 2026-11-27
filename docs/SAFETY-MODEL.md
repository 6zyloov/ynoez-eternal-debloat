# Modelo de segurança do ynoez

## Regras obrigatórias
1. Toda ação mutável precisa de descrição, justificativa, pré-condições, risco e método de restauração.
2. Ler o estado atual antes de qualquer alteração.
3. Mostrar prévia (dry-run) e solicitar consentimento.
4. Criar backup/exportação quando aplicável e registrar resultado em log local.
5. Não executar lote de tweaks sem mostrar cada item e permitir desmarcá-lo.
6. Se hardware, revisão ou versão do Windows forem desconhecidos, bloquear ajustes específicos.
7. Se não for possível ler o estado com confiança, não presumir nem forçar um valor.
8. Não baixar executáveis de espelhos desconhecidos; preferir fontes oficiais.
9. Não apagar perfil de navegador, senhas, cookies, extensões ou documentos.
10. Modo AFK não encerra processos essenciais, jogos, scripts, downloads ou tarefas escolhidas.

## Componentes protegidos — não desligar/remover por padrão
- Microsoft Defender / Windows Security.
- Windows Update e componentes de atualização/recuperação.
- TPM 2.0, Secure Boot, VBS, HVCI/Integridade da memória e IOMMU/DMA remapping.
- Drivers/serviços necessários a boot, rede, áudio, USB, armazenamento e Plug and Play.
- Componentes necessários ao Riot Vanguard e FACEIT Anti-Cheat.
- Microsoft Store, impressão, Calculadora, Clipboard, Windows Search, Bloco de Notas e NVIDIA Instant Replay/Alt+Z.

O programa pode diagnosticar esses componentes, mas não oferecer um botão genérico para desligá-los.

## Classificação
### Seguro
Diagnósticos read-only; abrir configurações oficiais; limpeza de temporários identificados com prévia; ajustes documentados com restauração.

### Avançado
Inicialização de serviços não essenciais após verificar dependências; propriedades avançadas de energia/rede; controle de ventoinha por interface suportada; orientação de XMP somente com confirmação e plano de recuperação.

### Experimental / bloqueado
Desativar Defender, Windows Update, TPM, Secure Boot, VBS/HVCI ou IOMMU; tweaks BCD de timer/tick, HPET ou NDU sem evidência; desligar IPv6 globalmente; overclock/undervolt automático; flash automático de BIOS; remoção massiva de AppX/serviços.

## Anti-cheat
Manter intactos Secure Boot, TPM, HVCI/VBS e serviços de segurança. Verificar documentação atual dos fornecedores; não presumir que desligar proteções seja requisito. Em conflito de driver, oferecer diagnóstico e atualização oficial.

## Logs e restauração
Toda execução mutável deve registrar data/hora, ação, estado anterior/solicitado, resultado, erro, backup e necessidade de reinicialização. Logs locais, sem envio automático.
