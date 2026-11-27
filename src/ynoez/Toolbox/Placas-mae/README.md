# Perfis de placas-mãe

Os perfis desta pasta descrevem um modelo e suas revisões físicas. O aplicativo só carrega um perfil quando fabricante e modelo SMBIOS correspondem. BIOS específica nunca é escolhida por aproximação.

## Cadastro
1. Confirmar nome comercial, identificadores SMBIOS e revisões na documentação oficial.
2. Cadastrar URLs oficiais independentes para cada revisão.
3. Listar drivers e utilitários oferecidos pelo fabricante para a revisão/Windows.
4. Descrever quais ferramentas podem alterar clock, tensão, firmware, fan curves ou recursos de segurança.
5. Manter ações sensíveis como consulta manual; não adicionar comandos de BIOS/overclock/segurança às rotinas automáticas.
6. Validar o JSON e testar a detecção em hardware identificado antes de habilitar ações.

O primeiro perfil é \`gigabyte-a520m-k-v2.json\`. Não deve ser duplicado e renomeado para outras placas sem nova pesquisa.
