# PresentMon no ynoez

- Versão fixada: 2.6.0.
- Fonte oficial: https://github.com/GameTechDev/PresentMon/releases/tag/v2.6.0
- Asset: `PresentMon-2.6.0-x64.exe`.
- SHA-256 fixado: `b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af`.
- Download apenas quando a pessoa inicia um benchmark; o arquivo é verificado antes de ser movido para `Toolbox/Performance/PresentMon.exe`.
- Licença MIT e `THIRD_PARTY.txt` oficial são incluídos em `Toolbox/Performance`.
- A captura usa ETW por PID, grava CSV em `Logs`, dura 30, 60, 120 ou 180 segundos e encerra quando o tempo termina ou o processo alvo fecha.
- A captura pode solicitar UAC para permissão ETW; o ynoez não instala driver de kernel.
- FPS médio = 1000 / média de `MsBetweenPresents`.
- 1% low apresentado = 1000 / P99 de `MsBetweenPresents`. A fórmula é explícita para evitar confusão entre definições de 1% low.
- Também são apresentados frametime médio, P99 e contagem de frames acima de 33,3 ms e 50 ms.
- A ferramenta mede; não atribui automaticamente ganhos a um tweak. Comparação automática antes/depois ainda está pendente.
