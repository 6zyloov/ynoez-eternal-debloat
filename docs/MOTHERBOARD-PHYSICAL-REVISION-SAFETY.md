# Seleção segura da revisão física da placa-mãe

## Política

A GIGABYTE A520M K V2 possui páginas de suporte separadas para as revisões cadastradas (1.0, 1.1 e 1.2). O campo `Win32_BaseBoard.Version` é tratado somente como indício informativo: não seleciona, confirma ou libera links de BIOS.

Na página Motherboard, o usuário precisa:

1. Ler a revisão impressa fisicamente na própria placa (serigrafia do PCB).
2. Escolher a revisão correspondente na lista apresentada.
3. Pressionar "Confirmar revisão física".

Somente depois da confirmação o ynoez exibe a versão de BIOS do perfil e os links oficiais específicos daquela revisão. Alterar a seleção remove a confirmação anterior. Se não houver confirmação, a página genérica de suporte e o manual continuam disponíveis, mas links de BIOS por revisão ficam ocultos.

## Validação

O serviço aceita apenas os identificadores exatos cadastrados no perfil, após remover espaços externos: 1.0, 1.1 ou 1.2. Não aceita texto de BIOS, versão de firmware, revision strings arbitrárias ou revisões não cadastradas.

## Limites

A confirmação é fornecida pelo usuário com base na serigrafia do PCB; o aplicativo não usa câmera nem tenta inferir a revisão a partir de uma fotografia. O ynoez continua sem baixar ou gravar BIOS, fazer downgrade, alterar tensão/clock/PBO ou reiniciar o computador.
