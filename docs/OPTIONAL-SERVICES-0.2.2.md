# Serviços opcionais — política 0.2.2

O painel não oferece um botão de desligar tudo. A lista é fixa e limitada a:

| Serviço | Função preservada se não desativar | Risco |
|---|---|---|
| DiagTrack | Dados de diagnóstico conectado | Médio |
| dmwappushservice | WAP Push e alguns cenários de gerenciamento | Médio |
| MapsBroker | Mapas offline | Baixo |
| Fax | Fax do Windows | Baixo |
| RetailDemo | Demonstração de varejo | Baixo |
| WMPNetworkSvc | Compartilhamento de mídia do Windows Media Player | Baixo |

Cada item exige confirmação e UAC. O backup registra StartMode, DelayedAutoStart e se estava em execução. A operação tenta parar o serviço sem forçar a parada de dependentes; se falhar, o backup permanece. A restauração verifica modo e estado anterior.

Protegidos e ausentes da whitelist: Windows Search, Spooler/print, Store/licenciamento, Defender/SecurityHealth, Windows Update/Update Orchestrator/BITS/Delivery Optimization, rede, áudio, energia, RPC, Event Log, Task Scheduler, Vanguard e FACEIT.

A lista pode variar conforme edição/build do Windows. Serviço inexistente é omitido. Nenhuma alteração foi validada em hardware real nesta etapa; a CI testa somente a whitelist, não desativa serviços no runner.
