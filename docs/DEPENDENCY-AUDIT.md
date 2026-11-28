# Auditoria de dependências do ynoez

O build executa a auditoria nativa do NuGet para dependências diretas e transitivas.

- `NuGetAudit` fica habilitado explicitamente.
- O limiar é moderado: avisos NU1901–NU1904 tornam-se erros e bloqueiam o build até a dependência ser atualizada, substituída ou a exceção ser analisada.
- A auditoria é executada no restore do app e do projeto de testes em CI e também no build local com o SDK .NET 10.
- O pipeline publica somente os artefatos gerados depois que restore, testes, publish, instalador e verificação de checksums terminam com sucesso.

Isto não substitui uma SBOM, revisão manual de licenças, assinatura Authenticode nem análise de segurança dinâmica. A ausência de avisos significa apenas que a fonte de advisories do NuGet não reportou vulnerabilidades moderadas ou superiores para o grafo restaurado naquela execução.
