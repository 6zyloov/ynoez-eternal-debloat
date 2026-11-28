# SBOM, integridade e assinatura de releases

## SBOM SPDX

O pipeline gera uma SBOM SPDX 2.2 usando o Microsoft.Sbom.DotNetTool, fixado na versão 4.1.13. O gerador recebe a pasta dist como conteúdo binário publicado e src/ynoez como raiz dos componentes para detectar o grafo de dependências NuGet do aplicativo, sem misturar os pacotes exclusivos do projeto de testes.

O processo valida o manifesto com o próprio Microsoft SBOM Tool, confirma a versão SPDX, exige pacotes detectados e verifica o namespace do documento. O arquivo final artifacts/ynoez-X.Y.Z.spdx.json é publicado como asset separado e incluído no SHA256SUMS.txt.

A SBOM registra componentes e arquivos detectados. Ela não é uma atestação de build e não substitui análise dinâmica, auditoria de vulnerabilidades, validação de licenças ou revisão humana de avisos de terceiros. A auditoria NuGet continua sendo uma etapa independente.

## Authenticode

O pipeline aceita um certificado de assinatura de código em formato PFX por meio dos secrets de Actions:

- YNOEZ_SIGNING_CERTIFICATE_BASE64: conteúdo do PFX codificado em Base64.
- YNOEZ_SIGNING_CERTIFICATE_PASSWORD: senha do PFX.

O PFX precisa conter uma chave privada e um certificado com EKU Code Signing. Em runner Windows, o script importa o certificado para o repositório temporário do usuário, assina o executável principal com SHA-256, aplica carimbo de tempo RFC 3161 e executa signtool verify /pa /all. O instalador é construído somente depois da assinatura do executável, garantindo que a cópia instalada também contenha a assinatura. Em seguida, o próprio instalador é assinado e verificado. O PFX e a entrada temporária do certificado são removidos ao final de cada etapa.

Não coloque o PFX, a senha ou a versão Base64 do certificado no Git, em artefatos ou em logs. Configure-os somente como secrets de Actions no repositório.

Quando os dois secrets não estiverem configurados, o pipeline não inventa nem simula uma assinatura: publica SIGNATURE-STATUS.txt explicitando Authenticode NOT SIGNED e mantém a verificação SHA-256 como mecanismo de integridade. Se apenas um secret estiver configurado, ou se o certificado for inválido, a etapa falha em vez de produzir uma release com estado ambíguo.

## Migração e rollback do instalador

A CI constrói separadamente o aplicativo e o instalador da branch main e o candidato. O smoke test instala o pacote baseline, grava marcadores em Data, Backups, Logs e Downloads, atualiza para o candidato, faz rollback ao baseline, aplica o candidato novamente e desinstala. Em cada troca, compara o SHA-256 do ynoez.exe instalado com o executável esperado e verifica que todos os marcadores continuam presentes.

Esse cenário testa migração entre o baseline atual de main e o candidato; não deve ser descrito como upgrade de uma release estável anterior. O repositório ainda não possui uma release estável publicada. A primeira migração entre versões realmente publicadas só poderá ser testada depois que existir uma versão anterior distribuída.

## Limite de validação

A CI roda em Windows Server 2025. Ela não substitui execução em Windows 11 cliente, testes prolongados de Roblox + XT, medição com PresentMon em jogos reais, sensores físicos, redes reais ou verificação autorizada com Vanguard/FACEIT. Esses itens permanecem como critérios de saída do candidato.
