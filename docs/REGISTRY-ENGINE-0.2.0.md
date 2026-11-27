# Contrato do motor de ajustes (0.2.0)

## Garantias implementadas
- Escopo restrito a valores HKCU em caminhos explicitamente permitidos.
- Nenhuma alteração é aplicada por carregar ou visualizar o catálogo.
- O backup do valor original é gravado em arquivo temporário e movido atomicamente antes da escrita no Registro.
- O tipo original (DWORD ou texto) e a existência anterior do valor são preservados.
- A aplicação faz leitura de verificação após a escrita.
- A restauração repõe o valor original ou remove somente o valor criado pelo ynoez.
- A restauração pode ser repetida com segurança enquanto o backup existir; o backup é consumido após sucesso.

## Limites e próximos passos
- O catálogo ainda não está conectado aos controles da interface; nesta etapa foi entregue o núcleo de transação.
- O motor atual aceita apenas HKCU, REG_DWORD e REG_SZ. HKLM, serviços, tarefas agendadas, AppX e políticas de máquina exigem adaptadores próprios e revisões de segurança.
- A exclusão de chave vazia é conservadora; se a chave não puder ser removida por conter outros valores/subchaves, ela permanece.
- O arquivo de backup contém dados de Registro do perfil do usuário e deve permanecer local.
- Antes de ampliar o catálogo, cada item deve ter detecção, justificativa, compatibilidade, pré-condições, teste e restauração verificados.
- O motor ainda precisa de testes automatizados em Windows e teste de falhas (permissão negada, arquivo corrompido, chave ausente, encerramento abrupto).

## Itens de catálogo
O catálogo inicial contém quatro ajustes opcionais e reversíveis de sugestões/captura. Eles não são aplicados automaticamente. A captura do Windows é separada do NVIDIA Instant Replay.
