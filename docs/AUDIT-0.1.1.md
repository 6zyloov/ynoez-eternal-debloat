# Auditoria técnica — ynoez 0.1.1

Auditoria estática da fonte existente em 27/11/2026. Não é teste funcional em Windows real.

## Estado real

A 0.1.1 é uma fundação executável: shell WPF, inventário WMI básico, snapshots locais, alternância do plano de energia, ping pontual, limpeza de caches e temporários, Toolbox de atalhos e perfil inicial da A520M K V2. Ela ainda não é um otimizador completo.

## Pontos fortes

- Aplicativo nativo WPF/.NET 8, sem depender de uma página web para a UI.
- Estrutura local concentrada em uma raiz `ynoez`.
- Lista explícita de componentes e proteções que não devem ser removidos.
- Limpeza de caches com recusa a reparse points e preservação dos diretórios de perfil.
- O plano de energia anterior é salvo antes de uma alteração.
- Operações atuais não desativam Defender, Windows Update, TPM, Secure Boot, VBS/HVCI, IOMMU/DMA, Vanguard ou FACEIT.

## Débitos técnicos encontrados

1. **MainWindow.xaml.cs concentra UI e lógica:** precisa ser dividido em ViewModels, serviços e componentes testáveis.
2. **Sem suíte de testes automatizados:** não há testes unitários para parsing, catálogo, detecção de estado, backup/undo, limpeza ou diagnósticos.
3. **Backup parcial:** snapshot atual guarda inventário, plano de energia e saída de ipconfig; não captura transações individuais de registro, serviço, tarefa agendada, driver ou adaptador.
4. **Ponto de restauração não é backup transacional:** o sucesso do comando não substitui backup próprio nem garante que todos os tipos de configuração sejam restauráveis.
5. **Rede é apenas uma amostra curta:** 8 pings ao gateway, 10 ao IP externo e uma consulta DNS. Não mantém histórico contínuo, não testa múltiplos destinos/IPv6/rota e não isola perda em intervalos longos. O cancelamento não é propagado à chamada ICMP atual.
6. **Medição de jitter limitada:** média da diferença absoluta entre respostas consecutivas; precisa ser documentada e complementada com distribuição, percentis e timestamps.
7. **Identificação da placa:** SMBIOS pode não fornecer a revisão física do PCB. A versão deve ficar como “não confirmada” até leitura confiável ou confirmação do usuário.
8. **Perfil de BIOS é estático:** precisa de data de consulta, URL oficial por revisão, CPU suportada, hash do download quando disponível e alerta de que a atualização é manual.
9. **Caminhos e permissões:** a raiz deriva de AppContext.BaseDirectory. A instalação em Program Files ou execução sem permissão de escrita exige separar binários de dados graváveis; o destino padrão C:\ynoez evita parte do problema, mas precisa de teste.
10. **Fonte e dependências:** WPF não inclui automaticamente a fonte Poppins em todas as instalações. A UI precisa de fallback tipográfico e pacote/aviso de licença da fonte se ela for distribuída.
11. **Processos e elevação:** execução elevada deve ocorrer apenas para operações que precisam disso; não elevar toda a interface sem motivo.
12. **Diagnóstico de GPU/telemetria:** WMI não basta para métricas confiáveis de clock, potência, temperatura, PCIe, DPC e frametime. Precisamos de providers documentados e fallbacks.
13. **Ausência de testes de compatibilidade:** não foram testados Windows 10/11, builds diferentes, contas sem admin, Windows em português, Secure Boot, VBS/HVCI, Vanguard ou FACEIT.
14. **Empacotamento:** o publish single-file autocontido é grande; avaliar trimming/AOT somente depois de validar System.Management/WPF e manter pacote de diagnóstico/recuperação.

## Critérios de aceite

- Toda ação tem `Detect`, `Plan`, `Apply`, `Verify`, `Undo` ou é declarada não reversível.
- A interface exibe o estado lido do Windows, não um toggle salvo localmente.
- A operação não inicia se o sistema não satisfizer suas pré-condições.
- Falhas parciais produzem rollback ou instrução de recuperação específica.
- O log registra valores antes/depois, horário, versão do ynoez e resultado, sem gravar segredos.
- Testes automatizados cobrem o plano e a reversão; testes de integração rodam em VMs Windows.
- Nenhum perfil toca componentes protegidos por padrão.
