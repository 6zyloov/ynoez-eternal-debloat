[CmdletBinding()]
param([switch]$DryRun)

$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$Tag = $env:GITHUB_REF_NAME
$Repository = $env:GITHUB_REPOSITORY

[xml]$project = Get-Content (Join-Path $ProjectRoot 'src\ynoez\ynoez.csproj')
$version = [string]$project.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw 'O projeto não declara uma versão válida.'
}
if ($DryRun) {
    if ([string]::IsNullOrWhiteSpace($Tag)) { $Tag = "v$version" }
} else {
    if ([string]::IsNullOrWhiteSpace($Tag) -or $Tag -notmatch '^v[0-9]+\.[0-9]+\.[0-9]+

$dist = Join-Path $ProjectRoot 'dist'
$artifactDir = Join-Path $ProjectRoot 'artifacts'
$setup = Join-Path $artifactDir "ynoez-setup-$version.exe"
$portable = Join-Path $artifactDir 'ynoez-portable-win-x64.zip'
$checksums = Join-Path $artifactDir 'SHA256SUMS.txt'
$notes = Join-Path $ProjectRoot 'docs\RELEASE-NOTES.md'

foreach ($required in @((Join-Path $dist 'ynoez.exe'), $setup, $notes)) {
    if (-not (Test-Path $required -PathType Leaf)) {
        throw "Arquivo obrigatório ausente para release: $required"
    }
}

if (Test-Path $portable) { Remove-Item $portable -Force }
Compress-Archive -Path (Join-Path $dist '*') -DestinationPath $portable -CompressionLevel Optimal
if (-not (Test-Path $portable -PathType Leaf)) { throw 'O pacote portátil não foi gerado.' }

$assets = @($setup, $portable)
$hashLines = foreach ($asset in $assets) {
    $hash = (Get-FileHash -Path $asset -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([System.IO.Path]::GetFileName($asset))"
}
Set-Content -Path $checksums -Value $hashLines -Encoding ascii

$verifiedAssets = @($setup, $portable)
foreach ($asset in $verifiedAssets) {
    if ((Get-FileHash -Path $asset -Algorithm SHA256).Hash.Length -ne 64) {
        throw "Não foi possível calcular SHA-256 para $asset."
    }
}
if (-not (Test-Path $checksums -PathType Leaf) -or (Get-Content $checksums).Count -ne 2) {
    throw 'O manifesto SHA256SUMS não contém exatamente os dois arquivos publicados.'
}

if ($DryRun) {
    Write-Host "Dry-run concluído: tag $Tag, instalador, pacote portátil e SHA256SUMS validados. Nenhuma release foi criada." -ForegroundColor Green
    return
}

& gh release create $Tag $setup $portable $checksums --repo $Repository --title ("ynoez " + $version) --notes-file $notes --verify-tag
if ($LASTEXITCODE -ne 0) {
    throw "GitHub CLI não conseguiu criar a release $Tag (código $LASTEXITCODE)."
}

Write-Host "Release $Tag publicada com instalador, pacote portátil e SHA256SUMS." -ForegroundColor Green
) {
        throw "Release precisa ser acionada por uma tag estável vMAJOR.MINOR.PATCH. Valor recebido: '$Tag'."
    }
    if ([string]::IsNullOrWhiteSpace($Repository)) {
        throw 'GITHUB_REPOSITORY não foi fornecido.'
    }
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw 'GitHub CLI (gh) não está disponível no runner.'
    }
    if ([string]::IsNullOrWhiteSpace($env:GH_TOKEN)) {
        throw 'GH_TOKEN não foi fornecido; a release não será criada sem autenticação.'
    }
}
if ($Tag -ne "v$version") {
    throw "Tag $Tag não corresponde à versão do projeto '$version'. Atualize .csproj e installer antes de publicar."
}

$dist = Join-Path $ProjectRoot 'dist'
$artifactDir = Join-Path $ProjectRoot 'artifacts'
$setup = Join-Path $artifactDir "ynoez-setup-$version.exe"
$portable = Join-Path $artifactDir 'ynoez-portable-win-x64.zip'
$checksums = Join-Path $artifactDir 'SHA256SUMS.txt'
$notes = Join-Path $ProjectRoot 'docs\RELEASE-NOTES.md'

foreach ($required in @((Join-Path $dist 'ynoez.exe'), $setup, $notes)) {
    if (-not (Test-Path $required -PathType Leaf)) {
        throw "Arquivo obrigatório ausente para release: $required"
    }
}

if (Test-Path $portable) { Remove-Item $portable -Force }
Compress-Archive -Path (Join-Path $dist '*') -DestinationPath $portable -CompressionLevel Optimal
if (-not (Test-Path $portable -PathType Leaf)) { throw 'O pacote portátil não foi gerado.' }

$assets = @($setup, $portable)
$hashLines = foreach ($asset in $assets) {
    $hash = (Get-FileHash -Path $asset -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([System.IO.Path]::GetFileName($asset))"
}
Set-Content -Path $checksums -Value $hashLines -Encoding ascii

& gh release create $Tag $setup $portable $checksums --repo $Repository --title ("ynoez " + $version) --notes-file $notes --verify-tag
if ($LASTEXITCODE -ne 0) {
    throw "GitHub CLI não conseguiu criar a release $Tag (código $LASTEXITCODE)."
}

Write-Host "Release $Tag publicada com instalador, pacote portátil e SHA256SUMS." -ForegroundColor Green
