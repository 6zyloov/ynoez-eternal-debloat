# Toolbox — inventário de tarefas agendadas

## Escopo

A seção "Inventário ao vivo de tarefas agendadas" executa uma consulta somente leitura com o módulo PowerShell `ScheduledTasks`. Para cada tarefa que o Windows retorna, mostra:

- caminho e nome da tarefa;
- estado (pronta, em execução, desativada ou na fila);
- autor e descrição;
- hora da última execução e próxima execução, quando disponíveis;
- último código de resultado.

A UI permite pesquisar por nome, caminho, estado, autor, descrição ou classificação, abrir o console oficial `taskschd.msc`, selecionar uma tarefa para ver detalhes e exportar CSV.

## Classificação conservadora

- **Protegida pelo ynoez:** tarefas de Windows Update, Update Orchestrator, Defender, manutenção, diagnóstico, pesquisa, rede e anti-cheats conhecidos. A lista é uma salvaguarda de interface, não uma análise completa de dependências.
- **Opcional avaliada:** somente as seis tarefas que já pertencem à whitelist reversível do Debloat: Consolidator, UsbCeip, DmClient, DmClientOnScenarioDownload, MapsToastTask e MapsUpdateTask. A classificação não as desativa.
- **Somente leitura:** qualquer outra tarefa sem política explicitamente aprovada.

A classificação das tarefas opcionais explícitas precede a regra de proteção de pasta: isso permite que Consolidator e UsbCeip sejam reconhecidas como whitelisted mesmo estando sob a pasta Customer Experience Improvement Program. O inventário continua sem oferecer ação de alteração.

## Segurança e privacidade

A consulta usa PowerShell codificado e grava um relatório JSON temporário em `Data`, que é removido ao fim da operação. O relatório contém apenas nome/caminho, estado, autor, descrição e horários/resultado; não exporta principal/conta de logon nem argumentos de ação que poderiam conter dados sensíveis.

A operação possui timeout de 45 segundos, cancelamento e limite de 30 MB para o arquivo de resposta. A saída CSV neutraliza fórmulas iniciadas por caracteres especiais.

## Limitações

- O Agendador pode mudar a lista e os horários logo após a consulta; trata-se de snapshot, não monitoramento contínuo.
- Última/próxima execução e último resultado dependem de `Get-ScheduledTaskInfo`; alguns itens protegidos podem não fornecer dados por restrições do Windows.
- A classificação conservadora não prova que uma tarefa de terceiros seja maliciosa ou inútil; tarefas desconhecidas permanecem somente leitura.
- A tela não executa, termina, habilita, desabilita, exclui ou altera tarefas.
