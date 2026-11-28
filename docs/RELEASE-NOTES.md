# ynoez 0.3.1 — candidato

Não é release estável: ainda precisa compilar na CI, passar nos testes e ser validado em Windows real.

## Corrigido
- Core Performance Boost / Turbo Boost deixou de ser tratado como requisito de anti-cheat; virou opção separada de desempenho.
- Lenovo WMI, ASUS ACT e o perfil GIGABYTE A520M K V2 voltam a ser detectados (comparação de texto estava com caixa errada).
- Gravação na BIOS agora exige confirmação explícita e nunca reinicia sozinha.

## Novo
- Abertura com o sigilo: um ponto no centro desabrocha em partículas até formar o sigilo; clique pula a animação.
- Diálogos escuros no estilo do painel no lugar da caixa clara do Windows.
- Relatório do Debloat: removido, desativado, ignorado, mantido e falhas, com aviso de que AppX não tem restauração automática.
- "Desfazer otimização" na página Otimizar (restaura apenas o que tem backup).

## Interno
- Remoção de ~1.700 linhas de interface inalcançável do `MainWindow`.
- Documentação reescrita para refletir o build real; veja `docs/AUDIT-0.3.1.md`.