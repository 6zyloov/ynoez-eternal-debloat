# Testes de integração Windows

O projeto `tests/ynoez.Tests` executa testes em runner Windows e usa somente uma subchave isolada e exclusiva em HKCU:
`Software\Microsoft\Windows\CurrentVersion\ynoez-tests\{guid}`.

Cobertura inicial:
- Aplicar um DWORD sobre valor existente e restaurar o valor original.
- Aplicar um valor inexistente e confirmar que a restauração o remove.
- Confirmar que o catálogo carrega IDs únicos e apenas valores DWORD HKCU.

Cada teste limpa a chave e a pasta temporária no bloco `finally`. A CI executa os testes antes de publicar o executável. Isso não substitui validação em máquinas físicas nem testes de compatibilidade com Vanguard/FACEIT.
