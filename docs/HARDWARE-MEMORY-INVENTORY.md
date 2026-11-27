# Inventário de módulos de memória RAM

## Dados coletados

O cartão "Módulos de memória RAM" da Home consulta, sob ação do usuário, a classe WMI `Win32_PhysicalMemory` e o resumo de `Win32_PhysicalMemoryArray`. Quando o firmware disponibiliza os campos, o ynoez mostra:

- posição `DeviceLocator` e `BankLabel`;
- capacidade de cada módulo;
- tipo de memória conforme o código SMBIOS (por exemplo, DDR4/DDR5);
- velocidade reportada e `ConfiguredClockSpeed`;
- formato físico (DIMM/SO-DIMM);
- largura de dados/total e indicador ECC reportado;
- fabricante e part number.

A consulta é somente leitura e pode ser exportada em CSV. Não exige elevação administrativa, não altera XMP/DOCP, clocks, tensão, BIOS ou Registro.

## Interpretação correta

O número de dispositivos reportado por `Win32_PhysicalMemoryArray` é informação do firmware, não garantia de slots físicos utilizáveis. `DeviceLocator`, `BankLabel`, frequência e part number podem estar ausentes ou imprecisos em alguns firmwares.

O inventário não é uma leitura direta dos módulos SPD. Os campos de velocidade são os valores que o firmware expõe ao Windows e podem não incluir todos os perfis JEDEC/XMP/DOCP.

O Windows não disponibiliza por essa classe um indicador universal e confiável de single/dual/quad channel. Por isso o ynoez não afirma "dual channel" apenas porque encontrou dois módulos ou dois nomes de slot. Confirme topologia de canais no BIOS/UEFI ou por ferramenta oficial do fabricante antes de tirar essa conclusão.

## Testes

Os testes unitários cobrem mapeamento de tipos SMBIOS, formatos físicos, capacidade, ECC reportado, descrição de dados ausentes, cálculo agregado e neutralização de fórmulas no CSV. A CI compila em Windows e roda a suíte; a consulta WMI real ainda precisa ser verificada no hardware de destino.
