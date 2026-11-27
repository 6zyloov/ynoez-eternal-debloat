# Inventário PnP de rede, áudio e dispositivos de sistema

## Coleta

A Central da placa-mãe oferece uma consulta sob demanda a duas classes WMI:

- `Win32_PnPEntity`: nome do dispositivo, ID PnP, classe, fabricante, estado, código de configuração, serviço e HardwareIDs.
- `Win32_PnPSignedDriver`: provedor, versão, data e nome do arquivo INF do driver que o Windows associa ao ID do dispositivo.

A UI mostra dispositivos nas classes `Net`, `MEDIA` e `System`. O usuário pode selecionar um item para ver os IDs de hardware completos e exportar CSV.

## Interpretação

- O inventário ajuda a identificar o controlador realmente presente (por exemplo, o hardware LAN e o codec/controlador de áudio) antes de abrir a página de drivers da Gigabyte.
- O provedor e a versão apresentados são os metadados que o Windows retorna para aquele dispositivo; não são uma avaliação automática de que o driver é o mais recente ou o mais adequado.
- Um código ConfigManager diferente de zero é mostrado com a descrição conhecida, mas não é reparado pelo inventário.
- Dispositivos de áudio HDMI da GPU podem aparecer junto ao codec onboard; compare nome e IDs antes de associar um item à placa-mãe.
- Para escolher BIOS/driver, confirme primeiro a revisão física do PCB. SMBIOS não seleciona a revisão por conta própria.

## Segurança

A coleta é somente leitura. Não chama Update-PnpDevice, pnputil, DISM de driver, instaladores, Windows Update ou qualquer mecanismo de gravação. Não altera driver, Registro, estado, prioridade ou serviço do dispositivo. O CSV neutraliza fórmulas de planilha.

## Limitações

A enumeração depende das classes/dispositivos que o Windows e o firmware publicam. Controladores ocultos, dispositivos USB temporariamente desconectados ou componentes sem pacote assinado podem não aparecer. A listagem de drivers é um snapshot no momento da consulta.
