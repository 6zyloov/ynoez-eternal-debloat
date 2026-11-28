# AFK protegido e configuração inválida

A automação distingue o usuário inativo de uma tarefa ativa. Jogos detectados e processos que o usuário cadastrar como protegidos usam o plano Equilibrado com uma solicitação de prevenção de suspensão enquanto a sessão está ativa. A economia agressiva só pode entrar quando nenhuma carga protegida estiver presente.

**Falha segura:** se os arquivos de configuração de jogos ou processos protegidos estiverem inválidos/corrompidos, a automação não assume que a lista está vazia. Em vez de usar o plano Economia, ela permanece no modo AFK protegido (plano Equilibrado + bloqueio de suspensão) e exibe o motivo no status. O arquivo é preservado para diagnóstico; salvar novamente a lista personalizada corrige apenas o arquivo do usuário, enquanto a configuração de jogos embarcada precisa ser validada pela aplicação.

Esse comportamento privilegia continuidade de jogos/robôs/downloads sobre economia de energia quando a aplicação não consegue determinar com confiança quais tarefas precisam de proteção.
