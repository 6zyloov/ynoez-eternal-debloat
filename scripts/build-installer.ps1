$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root "dist"
$artifactDir = Join-Path $root "artifacts"
$project = Join-Path $root "src\ynoez\ynoez.csproj"
$script = Join-Path $root "installer\ynoez.iss"

if (-not (Test-Path -LiteralPath $project)) { throw "Projeto não encontrado: $project" }
if (-not (Test-Path -LiteralPath $script)) { throw "Script do Inno Setup não encontrado: $script" }

[xml]$projectXml = Get-Content -Raw -LiteralPath $project
$version = [string]$projectXml.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version) -or $version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') {
    throw "Versão inválida no csproj: '$version'."
}

$issContent = Get-Content -Raw -LiteralPath $script
$versionMatch = [regex]::Match($issContent, '(?m)^#define MyAppVersion "([^"]+)"\s*$')
if (-not $versionMatch.Success -or $versionMatch.Groups[1].Value -ne $version) {
    throw "A versão do Inno Setup precisa corresponder ao csproj. Projeto='$version'; Inno='$($versionMatch.Groups[1].Value)'."
}

$setup = Join-Path $artifactDir "ynoez-setup-$version.exe"
if (-not (Test-Path (Join-Path $dist "ynoez.exe"))) {
    throw "dist\ynoez.exe não existe. Publique o aplicativo antes."
}

$isccCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
)
$iscc = $isccCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 (ISCC.exe) não foi encontrado." }

New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null
& $iscc $script
if ($LASTEXITCODE -ne 0) { throw "ISCC falhou com código $LASTEXITCODE." }
if (-not (Test-Path -LiteralPath $setup -PathType Leaf)) { throw "O instalador não foi gerado: $setup" }

Write-Host "Instalador $version gerado: $setup" -ForegroundColor Green
