# Perfis automáticos e AFK protegido

## Política

O ynoez distingue falta de entrada do usuário de ausência de trabalho útil. A ausência de mouse/teclado não significa que Roblox, um jogo, bot ou automação deixou de ser importante.

| Situação detectada | Perfil aplicado | Suspensão automática |
|---|---|---|
| Jogo conhecido com entrada recente | Performance | Conforme o plano do Windows |
| Jogo conhecido sem entrada recente | AFK protegido (plano Equilibrado) | Bloqueada enquanto a automação estiver ativa |
| Processo extra protegido em execução | AFK protegido (plano Equilibrado) | Bloqueada enquanto a automação estiver ativa |
| Sem jogo/processo protegido e com menos de 15 min sem entrada | Equilibrado | Conforme o Windows |
| Sem jogo/processo protegido e com 15 min ou mais sem entrada | AFK / Economia | Bloqueada enquanto o ynoez monitora a sessão |

O perfil AFK protegido não usa SCHEME_MAX; usa SCHEME_BALANCED e mantém uma solicitação SystemRequired para impedir a suspensão do sistema. A tela ainda pode apagar. Quando o processo protegido termina, a automação reavalia a situação e pode mudar para o perfil AFK Economia.

## Processos protegidos pelo usuário

A interface Tweaks permite cadastrar até 64 nomes de processos adicionais, separados por ponto e vírgula. O nome é a imagem do processo, sem espaços, caminho ou linha de comando; o sufixo .exe é opcional. Caminhos, espaços, argumentos, curingas e separadores são rejeitados.

A mesma tela pode atualizar um inventário somente leitura de processos em execução (nome, PID e memória de trabalho) e adicionar o nome selecionado à lista. O inventário não lê linha de comando/caminho, não abre handles para escrita e não encerra nem modifica processos.

Os hosts comuns de automação e script também são protegidos por padrão: cmd, Windows PowerShell/PowerShell 7, Python, Node.js, Java, Windows Script Host e AutoHotkey. A lista personalizada é somada à lista embarcada; salvar preferências nunca remove esses padrões. Jogos incluídos em Optimization/games.json, incluindo Roblox, são protegidos independentemente da lista personalizada.

As preferências personalizadas são gravadas em C:\ynoez\Data\auto-profile-settings.json. A lista é carregada ao iniciar uma nova sessão automática. Para ferramentas de automação que usem um processo próprio, adicione o nome exato do executável (sem caminho ou argumentos).

## Segurança e recuperação

Ao iniciar a automação, o ynoez exporta o plano original. Ao parar ou encerrar a sessão, restaura e verifica o plano original. Se a sessão for interrompida, os metadados e o arquivo .pow são preservados para recuperação na próxima abertura. O bloqueio de suspensão existe somente enquanto a sessão do ynoez está ativa.

A escolha do perfil é determinística e possui testes de unidade para jogos, processos protegidos, tempo de inatividade e validação dos nomes cadastrados. A CI Windows compila a aplicação e executa os testes; isso não substitui teste real em Roblox, XT ou outros processos do usuário.

## Limites

- Não impede suspensão manual, desligamento, tampa fechada ou perda de energia.
- O modo AFK manual continua sendo uma escolha explícita do usuário e pode reduzir o desempenho de processos que permanecerem abertos; a tela apresenta esse aviso.
- O perfil protegido usa o plano Equilibrado, não promete FPS, não reserva CPU exclusivamente para o jogo e não altera o agendador do Windows.
- A detecção depende do nome real do processo. Adicione o executável correto à lista protegida quando necessário.
