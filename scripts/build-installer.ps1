$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root "dist"
$artifactDir = Join-Path $root "artifacts"
$script = Join-Path $root "installer\ynoez.iss"
$isccCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 (ISCC.exe) não foi encontrado." }
if (-not (Test-Path (Join-Path $dist "ynoez.exe"))) { throw "dist\ynoez.exe não existe. Publique o aplicativo antes." }
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null
& $iscc $script
if ($LASTEXITCODE -ne 0) { throw "ISCC falhou com código $LASTEXITCODE." }
$project = Join-Path $root "src\ynoez\ynoez.csproj"
[xml]$projectXml = Get-Content $project
$version = [string]$projectXml.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version) -or $version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+
if (-not (Test-Path $setup)) { throw "O instalador não foi gerado." }
Write-Host "Instalador gerado: $setup"
) {
    throw "Versão inválida no csproj: '$version'."
}
$setup = Join-Path $artifactDir "ynoez-setup-$version.exe"
if (-not (Test-Path $setup)) { throw "O instalador não foi gerado." }
Write-Host "Instalador gerado: $setup"
