$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root "dist"
$artifactDir = Join-Path $root "artifacts"
$script = Join-Path $root "installer\ynoez.iss"
$projectFile = Join-Path $root "src\ynoez\ynoez.csproj"

if (-not (Test-Path $projectFile -PathType Leaf)) { throw "Projeto principal não encontrado: $projectFile" }
[xml]$project = Get-Content $projectFile
$version = [string]$project.Project.PropertyGroup.Version
try { $parsedVersion = [version]$version }
catch { throw "Versão do projeto inválida: '$version'." }
if ($parsedVersion.Build -lt 0 -or $parsedVersion.Revision -ne -1) {
    throw "Versão do projeto fora do formato MAJOR.MINOR.PATCH: '$version'."
}

$isccCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 (ISCC.exe) não foi encontrado." }
if (-not (Test-Path (Join-Path $dist "ynoez.exe"))) { throw "dist\ynoez.exe não existe. Publique o aplicativo antes." }
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null

# Remove only older installer outputs so a stale version cannot be packaged by mistake.
Get-ChildItem -Path $artifactDir -Filter "ynoez-setup-*.exe" -File -ErrorAction SilentlyContinue |
    Remove-Item -Force

$defineVersion = "/DMyAppVersion='$version'"
& $iscc $defineVersion $script
if ($LASTEXITCODE -ne 0) { throw "ISCC falhou com código $LASTEXITCODE." }

$setup = Join-Path $artifactDir "ynoez-setup-$version.exe"
if (-not (Test-Path $setup -PathType Leaf)) { throw "O instalador esperado não foi gerado: $setup" }

$fileInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($setup)
$versionQuad = "$version.0"
if ($fileInfo.FileVersion -ne $versionQuad) {
    throw "Versão PE do instalador ($($fileInfo.FileVersion)) difere do projeto ($versionQuad)."
}
if ($fileInfo.ProductName -ne "ynoez") {
    throw "Metadado ProductName do instalador não é ynoez."
}

Write-Host "Instalador $version gerado e metadados PE verificados: $setup"
