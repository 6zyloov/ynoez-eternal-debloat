# Reparo explícito de integridade do Windows

A página Restore oferece cinco ações oficiais e separadas:
- DISM /CheckHealth e /ScanHealth (somente leitura)
- DISM /RestoreHealth (reparo, requer confirmação)
- SFC /VerifyOnly (somente leitura)
- SFC /Scannow (reparo, requer confirmação)

Cada comando é fixo em uma lista permitida. O usuário não fornece executável ou argumentos. As operações solicitam UAC, gravam log individual em C:\\ynoez\\Logs, têm timeout/cancelamento e não reiniciam o computador. Uma operação DISM de reparo pode usar arquivos locais ou Windows Update, conforme o próprio Windows.

## Limites
- CI valida catálogo/allowlist e rejeição de ações desconhecidas; os testes não executam DISM/SFC durante a build.
- O resultado depende do estado do Windows, componentes disponíveis, internet/políticas corporativas e permissões.
- Não substitui backup, ponto de restauração, diagnóstico de hardware nem reparo de instalação.
