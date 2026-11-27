# Perfis de placas-mãe

## Objetivo
A central de hardware do ynoez identifica fabricante, modelo, revisão, BIOS, chipset e controladores. Cada perfil contém informações verificadas para aquela placa/revisão: drivers oficiais, utilitários, recursos integrados, monitoramento, diagnósticos e configurações reversíveis.

Não presumir que um utilitário funciona em todas as placas do fabricante. A ausência de uma ferramenta na página de suporte do modelo significa que ela não deve ser oferecida como compatível.

## Identificação
1. Fabricante e produto SMBIOS.
2. Versão/revisão SMBIOS, quando realmente preenchida.
3. Versão da BIOS.
4. IDs PnP dos dispositivos integrados.
5. Confirmação física da revisão quando SMBIOS for genérico ou ambíguo.

Nunca recomendar firmware com base apenas no chipset. O número da revisão deve corresponder à inscrição impressa na placa ou à identificação confiável do fabricante.

## Perfil inicial — GIGABYTE A520M K V2
Fontes oficiais para revisões 1.0, 1.1 e 1.2:
- [Rev. 1.0 — suporte](https://www.gigabyte.com/br/Motherboard/A520M-K-V2-rev-10/Support)
- [Rev. 1.1 — suporte](https://www.gigabyte.com/br/Motherboard/A520M-K-V2-rev-11/Support)
- [Rev. 1.2 — suporte](https://www.gigabyte.com/br/Motherboard/A520M-K-V2-rev-12/support)

Diferença importante: as páginas de recursos da GIGABYTE identificam Smart Fan 5 nas revisões 1.0 e 1.1, e Smart Fan 6 na 1.2. O perfil separa esses caminhos. Não aplicar curva de ventoinha sem confirmar a interface de controle.

A lista de recursos da revisão 1.2 menciona APP Center, EasyTune, @BIOS, Smart Backup, System Information Viewer e Q-Flash. A GIGABYTE avisa que aplicações disponíveis no APP Center e funções suportadas variam por modelo. A central do ynoez deve verificar a página de suporte da revisão e do Windows antes de apresentar downloads.

**LAN bandwidth management:** a página de especificações da GIGABYTE informa que o serviço do software incluído foi descontinuado por encerramento do contrato com o fornecedor. O ynoez não deve recomendar esse software como download atual.

A página da AMD para chipset A520 é a fonte preferencial do pacote de chipset. Em 27/11/2026, foi observada a versão 8.09.28.1124, publicada em 21/10/2026. É um registro histórico da verificação, não um valor fixo: o app deve consultar a AMD novamente antes de sugerir atualização.

A revisão 1.0 mostrava BIOS F7d datada de 17/11/2026 na verificação de 27/11/2026. Isso não deve ser transferido às revisões 1.1/1.2 nem apresentado como recomendação universal. A GIGABYTE alerta que atualização incorreta de BIOS pode inutilizar a placa.

## Ecossistemas de fabricantes pesquisados

| Fabricante | Central/utilitário | O que oferece segundo a documentação oficial | Regra para o ynoez |
|---|---|---|---|
| GIGABYTE | APP Center; utilitários por modelo | Portal de utilitários/drivers; em modelos listados aparecem @BIOS, EasyTune, Smart Backup, System Information Viewer e Q-Flash | Conferir revisão, sistema operacional e utilitário publicado para a placa |
| ASUS | Armoury Crate; AI Suite em modelos compatíveis | Controle centralizado de produtos ASUS/ROG suportados e recursos de placa conforme modelo | Armoury Crate não é universal; checar lista de suporte e página exata do produto |
| MSI | MSI Center | Feature Sets, monitoramento, User Scenario, Fan Center/Cooling Wizard, diagnóstico e Live Update, conforme modelo | A própria MSI informa que, se MSI Center não aparece na página Utility do produto, a placa não oferece suporte |
| ASRock | APP Shop; Auto Driver Installer em modelos compatíveis | Portal para drivers, BIOS e software; instaladores separados por placa/modelo | Só apresentar a ferramenta quando a tabela oficial incluir o modelo |

Fontes:
- [GIGABYTE A520M K V2 Rev. 1.2 — especificações](https://www.gigabyte.com/br/Motherboard/A520M-K-V2-rev-12/sp)
- [ASUS — Armoury Crate](https://www.asus.com/support/faq/1041776/)
- [MSI — introdução ao MSI Center](https://www.msi.com/support/technical_details/MB_SW_MSI_Center)
- [MSI — instalação limpa e regra de compatibilidade por página Utility](https://us.msi.com/support/technical_details/MB_SW_MSI_Center_Inst)
- [ASRock — APP Shop](https://www.asrock.com/feature/appshop/)
- [ASRock — lista oficial de utilitários por modelo](https://www.asrock.com/Support/index.asp?cat=Utilities)

## Expansão por fabricante
O escopo inicial de pesquisa inclui GIGABYTE, ASUS, MSI e ASRock. Cada placa precisa de um perfil individual; a tabela de ecossistemas não equivale a compatibilidade de todos os modelos.

Próximas fases:
1. ASUS: famílias AM4 e AM5; Armoury Crate/AI Suite e controladores integrados.
2. MSI: famílias AM4 e AM5; MSI Center/Dragon Center quando listado no suporte do modelo.
3. ASRock: famílias AM4 e AM5; APP Shop/Auto Driver Installer.
4. GIGABYTE: ampliar perfis por chipset, geração e revisão, mantendo ferramentas antigas apenas quando oficialmente suportadas.

## Critérios para publicar novo perfil
- Página oficial do modelo e lista de revisões.
- Especificações e controladores conferidos.
- Drivers por sistema operacional.
- Ferramentas e funções suportadas de fato documentadas.
- Riscos, pré-condições e rollback.
- Data de verificação.
- Separar utilitários do fabricante de recomendações próprias do ynoez.

Não copiar executáveis/driver proprietários para este repositório. Guardar links oficiais e metadados.
