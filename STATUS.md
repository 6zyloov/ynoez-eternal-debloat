# Desenvolvimento — ynoez 0.1.0

## Incluído
- Shell WPF com referência visual Stix: fundo #121212, cards #1E1E1E, destaque #FF80FF, Poppins, navegação lateral.
- Páginas Home, Backup, Tweaks, Network, Debloat, Motherboard, Toolbox, Restore e Logs.
- Inventário WMI de CPU, GPU, RAM, Windows, placa-mãe, BIOS e adaptadores.
- Snapshot local e tentativa explícita de ponto de restauração.
- Plano Alto desempenho com backup do plano anterior e restauração.
- Diagnóstico ICMP local/externo, perda, latência, jitter simples e DNS.
- Limpeza seletiva de Temp e caches Brave/Discord, sem seguir reparse points.
- Toolbox pesquisável, atalhos de configurações e leitura WMI de serviços relevantes.
- Perfil específico GIGABYTE A520M K V2, referências por revisão e políticas de segurança.
- Banco remoto de perfis e whitelist de componentes preservados.
- Scripts de build e instalação em C:\ynoez.

## Próximas etapas
1. Executar o primeiro build real no Windows via .NET 8 SDK ou GitHub Actions e corrigir erros do compilador.
2. Testar abertura, navegação, inventário WMI e ações seguras em Windows 10/11.
3. Implementar modos Performance / Equilibrado / AFK com processos protegidos, allowlist e rollback.
4. Adicionar monitoramento ao vivo e registro persistente de quedas de rede.
5. Adicionar mudanças individualmente reversíveis à Toolbox.
6. Expandir catálogo de placas após pesquisa oficial por modelo e revisão.
7. Fazer testes de instalação e adicionar ícone próprio.

## Estado de validação
O ambiente de desenvolvimento desta etapa não possui Windows nem .NET SDK. XML, JSON, estrutura, nomes de handlers XAML e análise estática foram verificados; **o código C# ainda não foi compilado nem executado**. Nenhuma otimização foi validada no computador do proprietário. Build aprovado não prova estabilidade em hardware real nem ganhos de FPS.
