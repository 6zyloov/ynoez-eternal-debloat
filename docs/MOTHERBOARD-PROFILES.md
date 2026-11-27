# Perfis de placas-mãe

## Objetivo
A central de hardware identifica fabricante, modelo, revisão, BIOS, chipset e controladores. Cada perfil contém apenas informações verificadas para aquela placa/revisão: drivers oficiais, utilitários, monitoramento, configurações de rede/energia e orientações de recuperação.

Não presumir que um utilitário funciona em todas as placas do fabricante.

## Identificação
1. Fabricante/produto SMBIOS.
2. Revisão SMBIOS quando preenchida.
3. Versão da BIOS.
4. IDs PnP dos dispositivos integrados.
5. Confirmação manual se a revisão não puder ser lida.

Se a revisão for desconhecida, não sugerir firmware. Não inferir revisão apenas pelo chipset.

## Perfil inicial — GIGABYTE A520M K V2
Revisões 1.0, 1.1 e 1.2. Cobertura: AMD Chipset Drivers, Realtek GbE LAN, Realtek Audio, APP Center, @BIOS, EasyTune, Smart Backup, System Information Viewer, Q-Flash e Smart Fan 5.

Os aplicativos disponíveis no APP Center e as funções suportadas variam por modelo. Não instalar o pacote completo automaticamente.

## Expansão por fabricante
| Fabricante | Ecossistema a pesquisar | Estado |
|---|---|---|
| GIGABYTE | APP Center, @BIOS, EasyTune, System Information Viewer, Q-Flash | Perfil inicial A520M K V2 |
| ASUS | Armoury Crate, AI Suite e utilitários por modelo | A pesquisar |
| MSI | MSI Center e recursos por modelo | A pesquisar |
| ASRock | APP Shop e utilitários por modelo | A pesquisar |
| Outros | Utilitário oficial documentado | A pesquisar |

A meta é criar perfis individuais e testados, não tweaks universais baseados apenas no chipset.

## Critérios para novo perfil
- Página oficial do modelo e lista de revisões.
- Especificações e controladores conferidos.
- Drivers por sistema operacional.
- Ferramentas e funções suportadas realmente documentadas.
- Riscos, pré-condições e restauração.
- Data de verificação.
- Separar ferramentas do fabricante de recomendações do ynoez.

Não copiar drivers ou executáveis proprietários para o repositório. Guardar links e metadados oficiais.
