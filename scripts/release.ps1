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
if ($Tag -ne "v$version") { throw "Tag '$Tag' não corresponde à versão do projeto '$version'." }
if (-not $DryRun) {
    if ([string]::IsNullOrWhiteSpace($Repository)) { throw 'GITHUB_REPOSITORY não foi fornecido.' }
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'GitHub CLI (gh) não está disponível no runner.' }
    if ([string]::IsNullOrWhiteSpace($env:GH_TOKEN)) { throw 'GH_TOKEN não foi fornecido; a release não será criada sem autenticação.' }
}

$dist = Join-Path $ProjectRoot 'dist'
$artifactDir = Join-Path $ProjectRoot 'artifacts'
$setup = Join-Path $artifactDir "ynoez-setup-$version.exe"
$portable = Join-Path $artifactDir 'ynoez-portable-win-x64.zip'
$sbom = Join-Path $artifactDir "ynoez-$version.spdx.json"
$signatureStatus = Join-Path $artifactDir 'SIGNATURE-STATUS.txt'
$checksums = Join-Path $artifactDir 'SHA256SUMS.txt'
$notes = Join-Path $ProjectRoot 'docs\RELEASE-NOTES.md'

foreach ($required in @((Join-Path $dist 'ynoez.exe'), $setup, $sbom, $signatureStatus, $notes)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Arquivo obrigatório ausente para release: $required"
    }
}

$sbomDocument = Get-Content -LiteralPath $sbom -Raw | ConvertFrom-Json
if ($sbomDocument.spdxVersion -ne 'SPDX-2.2' -or @($sbomDocument.packages).Count -lt 1) {
    throw 'A SBOM SPDX 2.2 está ausente, vazia ou inválida.'
}

if (Test-Path -LiteralPath $portable) { Remove-Item -LiteralPath $portable -Force }
Compress-Archive -Path (Join-Path $dist '*') -DestinationPath $portable -CompressionLevel Optimal
if (-not (Test-Path -LiteralPath $portable -PathType Leaf)) { throw 'O pacote portátil não foi gerado.' }

$assets = @($setup, $portable, $sbom, $signatureStatus)
$hashLines = @(
    foreach ($asset in $assets) {
        $hash = (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($hash -notmatch '^[a-f0-9]{64}$') { throw "Hash SHA-256 inválido para $asset." }
        "$hash  $([System.IO.Path]::GetFileName($asset))"
    }
)
Set-Content -LiteralPath $checksums -Value $hashLines -Encoding ascii

$manifestLines = @(Get-Content -LiteralPath $checksums)
if ($manifestLines.Count -ne $hashLines.Count) { throw 'O manifesto SHA256SUMS não corresponde à quantidade de arquivos publicados.' }
for ($index = 0; $index -lt $hashLines.Count; $index++) {
    if ($manifestLines[$index] -cne $hashLines[$index]) { throw 'O manifesto SHA256SUMS não corresponde aos hashes calculados.' }
}

if ($DryRun) {
    Write-Host "Dry-run concluído: tag $Tag, instalador, pacote portátil, SBOM SPDX 2.2, status Authenticode e SHA256SUMS validados. Nenhuma release foi criada." -ForegroundColor Green
    return
}

& gh release create $Tag $setup $portable $sbom $signatureStatus $checksums --repo $Repository --title ("ynoez " + $version) --notes-file $notes --verify-tag
if ($LASTEXITCODE -ne 0) { throw "GitHub CLI não conseguiu criar a release $Tag (código $LASTEXITCODE)." }

Write-Host "Release $Tag publicada com instalador, pacote portátil, SBOM SPDX 2.2, status Authenticode e SHA256SUMS." -ForegroundColor Green
