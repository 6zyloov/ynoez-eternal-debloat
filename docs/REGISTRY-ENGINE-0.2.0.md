# Motor de ajustes e transações (0.2.0)

## Garantias implementadas
- Escopo de escrita restrito a HKCU e a caminhos permitidos.
- Catálogo carregado e validado em tempo de execução; carregar ou visualizar não altera o sistema.
- Backup original escrito em arquivo temporário e movido atomicamente antes da alteração.
- Aplicação com leitura de verificação após a escrita.
- Remoção de valor de Registro com backup anterior, usada pelo gerenciador de inicialização.
- Restauração do valor ou remoção do valor criado pelo ynoez, seguida de verificação antes de consumir o backup.
- Preservação dos tipos originais DWORD, QWORD, REG_SZ, REG_EXPAND_SZ, REG_BINARY e REG_MULTI_SZ.
- IDs de backup validados; caminhos fora da whitelist rejeitados.

## Catálogo integrado
O painel Tweaks lê `Optimization/catalog.json`, detecta estado, exige confirmação individual e apresenta restauração quando há backup. Nenhum ajuste é aplicado em lote. Os quatro itens iniciais controlam sugestões de conteúdo e captura Game DVR do Windows. Eles não desativam NVIDIA Instant Replay/Alt+Z.

## Limites
- Somente HKCU nesta versão. HKLM, serviços, tarefas agendadas, AppX e políticas de máquina exigem adaptadores e revisão próprios.
- O catálogo inicial é intencionalmente pequeno. Cada novo ajuste deve incluir justificativa, risco, builds suportadas, pré-condições, detecção, aplicação, verificação e reversão.
- Backup corrompido ou incompatível interrompe a restauração sem apagar o arquivo de backup.
- Ainda são necessários testes adicionais de falha abrupta, permissões, concorrência e alteração externa simultânea.
