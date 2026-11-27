# Testes de integração Windows

O projeto `tests/ynoez.Tests` é executado em runner Windows e usa subchaves e pastas temporárias isoladas. As operações de Registro criam apenas uma subchave exclusiva em HKCU:
`Software\Microsoft\Windows\CurrentVersion\ynoez-tests\{guid}`.

Cobertura automatizada inclui:
- Transações HKCU: criar, alterar e restaurar valores DWORD, REG_EXPAND_SZ, binários, multi-string e QWORD; rejeitar caminhos não canônicos; preservar backups corrompidos.
- Snapshots locais: validar manifestos SHA-256, detectar alteração/tamanho divergente, rejeitar arquivos extras e caminhos fora da raiz de Backups.
- Catálogos e proteções: ajustes HKCU, whitelist de serviços/tarefas, exclusões de Windows Search, impressão, Store, Windows Update, Defender, NVIDIA, Vanguard e FACEIT.
- Startup: backup/reversão de entradas do usuário e arquivos temporários, sem tocar nos dados de inicialização da máquina.
- Energia e snapshots: cálculo de perfis, validação de exportações .pow e hashes/manifests de snapshots.
- Rede: perda e jitter em janelas móveis, diagnóstico heurístico de causa e estatísticas de adaptadores.
- Benchmark: parser do CSV PresentMon, FPS médio, 1%/0,1% lows e comparação de métricas.
- Inventários: classificação de componentes AppX e programas Win32, exportação CSV e neutralização de fórmulas em campos exportados.
- Atualizações: comparar versões estáveis, rejeitar hosts/tags inconsistentes e não confiar em URLs de release fornecidas fora do repositório oficial.

Cada teste de sistema cria dados isolados e limpa-os em `finally`. Nenhum teste deve alterar configurações reais de serviços, tarefas, adaptadores ou planos de energia da máquina do runner.

A CI executa os testes antes de publicar o binário e instalador. Isso não substitui testes manuais de interface, testes prolongados de rede, validação em hardware físico ou testes reais com Vanguard/FACEIT.
