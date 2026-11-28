# Foco de Jogo — ynoez 0.3.0

## Objetivo

Permitir que um jogo principal tenha prioridade de CPU e preferência de GPU de alto desempenho enquanto aplicações AFK (por exemplo, duas instâncias do Roblox) recebem prioridade de CPU extremamente baixa e prioridade de memória muito baixa, sem encerrar os processos.

## Configuração

1. Abra **Foco de Jogo** no menu lateral. O ynoez tenta detectar o CS2 nas bibliotecas Steam e a versão mais recente do Roblox automaticamente.
2. Clique em **Detectar CS2 + Roblox** para atualizar os caminhos. Se algum jogo não for localizado, abra-o uma vez ou use o seletor de executável.
3. O perfil recomendado já usa prioridade **Alta** para o jogo (com confirmação de segurança) e **Idle** para Roblox. Uma única entrada de `RobloxPlayerBeta.exe` abrange os dois clientes e novas instâncias.
4. Clique em **Ativar perfil de foco**. Feche e reabra os jogos para que a preferência gráfica do Windows seja considerada.
5. O XT não é rebaixado por um nome presumido: runtimes comuns como PowerShell, Python, Node.js, Java e AutoHotkey já são protegidos pelo perfil automático. Só cadastre o executável do XT aqui se ele for independente e você souber qual arquivo é.

## O que cada controle realmente faz

- **CPU do jogo:** `HIGH_PRIORITY_CLASS` ou `ABOVE_NORMAL_PRIORITY_CLASS`. Alta fica abaixo de Realtime e pode consumir quase todo o tempo de CPU; por isso exige confirmação e nunca deve ser confundida com uma reserva exclusiva de processador.
- **CPU dos bots:** `IDLE_PRIORITY_CLASS` por padrão. O Windows agenda essas threads quando processos de classes superiores deixam tempo disponível. A opção Abaixo do normal é um fallback para aplicações AFK que não toleram Idle.
- **Memória:** o jogo usa prioridade Normal e bots Very Low. Isso orienta o gerenciador de memória a remover páginas de prioridade mais baixa antes de páginas mais altas quando precisa reduzir working sets; não reserva toda a RAM ao jogo nem limita o consumo dos bots.
- **GPU:** o ynoez grava a preferência por aplicativo do Windows para adaptador de alto desempenho no jogo e economia nos bots. É uma escolha de adaptador, principalmente útil em sistemas com GPU integrada + dedicada. Não reserva a placa, VRAM ou um percentual de execução; em computadores com uma única GPU, a diferença pode ser nula.

## Segurança e restauração

- O ynoez não usa `REALTIME_PRIORITY_CLASS`, não muda afinidade de CPU, prioridades individuais de threads ou limites de working set.
- Processos essenciais do Windows e o próprio ynoez são ignorados.
- As prioridades originais são registradas por PID e horário de criação, evitando aplicar restauração a um processo diferente caso o Windows reutilize um PID.
- O perfil é reavaliado a cada dois segundos para alcançar novas instâncias do executável. As escolhas ficam salvas, mas exigem que você clique em Ativar perfil de foco em cada execução do ynoez; não são reaplicadas silenciosamente na abertura.
- O diário é salvo antes das mudanças nativas e de Registro. Ao desativar ou sair normalmente do ynoez, o aplicativo tenta restaurar os valores originais e desativa o perfil. Após um encerramento inesperado, a restauração é tentada na próxima abertura; a configuração é mantida, mas exige ativação manual.
- Enquanto o perfil estiver ativo, fechar a janela a oculta na bandeja para manter o monitoramento. Para encerrar completamente, use **Encerrar ynoez** no menu da bandeja; isso tenta restaurar as prioridades antes de sair.
- Se o Windows negar acesso ao processo, o ynoez mostra uma advertência. Executar como administrador pode ser necessário para processos elevados ou protegidos, mas não remove as restrições impostas por processos protegidos.

## Teste recomendado

Comece com CS2 em Alta (após ler e aceitar o aviso) e Roblox em Idle. Verifique se as duas instâncias continuam recebendo atualizações, se o Windows permanece responsivo e se o XT não perde a execução. Se houver falhas de áudio ou lentidão geral, use Acima do normal no CS2. Caso Roblox desconecte ou pare de avançar, mude o perfil AFK para Abaixo do normal. Faça uma sessão longa antes de considerar a configuração estável.

## Referências técnicas

- [Classes de prioridade e agendamento de CPU — Microsoft Learn](https://learn.microsoft.com/pt-br/windows/win32/procthread/scheduling-priorities)
- [SetPriorityClass — Microsoft Learn](https://learn.microsoft.com/pt-br/windows/win32/api/processthreadsapi/nf-processthreadsapi-setpriorityclass)
- [MEMORY_PRIORITY_INFORMATION — Microsoft Learn](https://learn.microsoft.com/pt-br/windows/win32/api/processthreadsapi/ns-processthreadsapi-memory_priority_information)
