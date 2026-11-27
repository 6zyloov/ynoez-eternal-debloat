# Fonte única de versão do ynoez

## Fonte

A versão estável do aplicativo é definida em um único lugar: a propriedade `<Version>` do arquivo `src/ynoez/ynoez.csproj`, no formato `MAJOR.MINOR.PATCH`.

## Propagação para o instalador

O script `scripts/build-installer.ps1` lê a versão do projeto, valida que ela tem três componentes numéricos e substitui marcadores explícitos no template `installer/ynoez.iss`, criando temporariamente `installer/ynoez.generated.iss` ao lado dele. O arquivo temporário contém valores literais (sem interpolação de versão do ISPP), é compilado e removido em `finally`. O instalador usa esses valores para:

- AppVersion e AppVerName;
- nome `ynoez-setup-X.Y.Z.exe`;
- VersionInfoVersion de quatro componentes (`X.Y.Z.0`).

Após compilar, o script verifica que o arquivo existe e que os metadados PE retornados por Windows correspondem à versão do projeto e ao ProductName `ynoez`. Instaladores antigos que poderiam confundir a validação são removidos da pasta de artefatos antes da compilação.

O workflow de build envia o arquivo gerado por padrão glob `ynoez-setup-*.exe`. O script de release já consulta a mesma propriedade do projeto e valida que a tag Git (`vX.Y.Z`) corresponde à versão antes de criar uma release.

## Validação

A CI Windows compila o instalador e executa o dry-run de release, que gera e valida o pacote portátil e o manifesto SHA-256. O build não cria uma release pública; isso só ocorre no workflow de tag após os mesmos testes.
