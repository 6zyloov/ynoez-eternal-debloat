[CmdletBinding()]
param([switch]$DryRun)

$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$Repository = $env:GITHUB_REPOSITORY

[xml]$project = Get-Content (Join-Path $ProjectRoot 'src\ynoez\ynoez.csproj')
$version = [string]$project.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version) -or $version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') {
    throw "Versão do projeto não está em formato MAJOR.MINOR.PATCH: '$version'."
}

$Tag = if ($DryRun) { "v$version" } else { $env:GITHUB_REF_NAME }
if ($Tag -ne "v$version") {
    throw "Tag '$Tag' não corresponde à versão do projeto '$version'."
}
if (-not $DryRun) {
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
if (-not (Test-Path $portable -PathType Leaf)) {
    throw 'O pacote portátil não foi gerado.'
}

$assets = @($setup, $portable)
$hashLines = @(
    foreach ($asset in $assets) {
        $hash = (Get-FileHash -Path $asset -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($hash -notmatch '^[a-f0-9]{64}$') {
            throw "Hash SHA-256 inválido para $asset."
        }
        "$hash  $([System.IO.Path]::GetFileName($asset))"
    }
)
Set-Content -Path $checksums -Value $hashLines -Encoding ascii

$manifestLines = @(Get-Content $checksums)
if ($manifestLines.Count -ne $hashLines.Count) {
    throw 'O manifesto SHA256SUMS não corresponde à quantidade de arquivos publicados.'
}
for ($index = 0; $index -lt $hashLines.Count; $index++) {
    if ($manifestLines[$index] -cne $hashLines[$index]) {
        throw 'O manifesto SHA256SUMS não corresponde aos hashes calculados.'
    }
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
