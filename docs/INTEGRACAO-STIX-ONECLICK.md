# Integração de conceitos: Stix + Oneclick + ynoez

## Princípio de implementação

O ynoez reimplementa conceitos de organização e operação. Não incorpora binários, scripts, marcas, assets ou trechos de código do Stix. A revisão disponível do Stix não encontrou uma licença explícita que autorizasse reutilização do código. O Oneclick é distribuído sob GPL-3.0; esta integração não copia seus scripts em lote nem o código do projeto, mas usa a organização de categorias e a documentação pública como referência.

## O melhor de cada referência

### Stix — organização e experiência
- Sidebar com áreas claras e páginas independentes.
- Home com resumo do hardware.
- Monitoramento e diagnósticos em telas específicas.
- Tweaks agrupados por categoria.
- Debloat Center e Network Tuning em vez de um único botão opaco.
- Backup e restauração acessíveis.
- Busca de ferramentas e configurações.

O ynoez preserva suas páginas existentes e fornece atalhos rápidos para as ações mais utilizadas, sem substituir o painel completo.

### Oneclick — amplitude de operação
- Perfis focados em otimização e manutenção.
- Separação por áreas: GPU, CPU, energia, latência, rede, serviços, tarefas e aplicativos.
- Logs locais transparentes e documentação de efeitos colaterais.
- Explicação de compatibilidade e de componentes que podem quebrar.

O ynoez adota a amplitude e a transparência, não a filosofia de desativar tudo indiscriminadamente.

### ynoez — camada de segurança e integração
- Catálogos allowlist; nada de enumerar todos os serviços e desligá-los genericamente.
- Backup antes da mudança, verificação do resultado e restauração quando suportada.
- Perfis e opções separados para não misturar requisito de anti-cheat com desempenho.
- Identificação real do sistema, placa, CPU, modo de boot e estado de segurança.
- Mudanças de firmware somente por interfaces específicas do fabricante e com confirmação.
- Logs locais e mensagens que distinguem “aplicado”, “ignorado”, “não suportado” e “falhou”.

## O que não copiar/ativar sem revisão

O Oneclick documenta efeitos colaterais relevantes em componentes Windows, incluindo conectividade Wi-Fi/Bluetooth/VPN, funções de Windows Security, Store/Xbox, impressão, navegadores, atualizações e recursos de economia de energia. Por isso, o ynoez não deve aplicar desativação indiscriminada dessas áreas.

O ynoez também não deve usar:
- desligamento de Defender, Firewall, UAC, Windows Update ou mecanismos de proteção;
- exclusão de Edge, Store, OneDrive, Xbox ou runtimes compartilhados;
- desligamento de Wi-Fi, Bluetooth, IPv6, DHCP, DNS, áudio, Spooler ou serviços de drivers;
- valores genéricos de registro/BCD/UEFI copiados de outra máquina;
- alterações de tensão, overclock automático ou limites térmicos;
- drivers de kernel ou ferramentas de terceiros sem integridade/verificação de origem.

## Limites do firmware

A compatibilidade automática é específica de fabricante/modelo e depende da interface oficial disponível. Lenovo WMI e ASUS ACT são os únicos caminhos tratados nesta versão. ASUS ACT só é considerado disponível quando o executável e o modelo suportado forem identificados. Em qualquer outro caso, o ynoez fornece orientação manual e não grava offsets UEFI genéricos.

O estado BitLocker desconhecido deve ser tratado como bloqueio, nunca como “desligado”. O aplicativo não deve reiniciar o PC nem entrar no setup sem ação explícita do usuário.

## Critério de qualidade

Nenhum ajuste será apresentado como ganho garantido de FPS. Cada tweak deve documentar:
1. O alvo e requisito de Windows/hardware.
2. O que muda e por que.
3. Risco e efeito colateral esperado.
4. Backup ou limitação de reversão.
5. Verificação posterior.
6. Como restaurar.
7. Evidência de teste ou indicação explícita de que falta teste no hardware real.

## Referências públicas

- Oneclick: https://github.com/QuakedK/Oneclick
- Auditoria do Stix registrada em `docs/STIX-SOURCE-AUDIT.md`.
