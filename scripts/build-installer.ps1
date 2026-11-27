$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root "dist"
$artifactDir = Join-Path $root "artifacts"
$installerDirectory = Join-Path $root "installer"
$templateScript = Join-Path $installerDirectory "ynoez.iss"
$generatedScript = Join-Path $installerDirectory "ynoez.generated.iss"
$projectFile = Join-Path $root "src\ynoez\ynoez.csproj"

if (-not (Test-Path $projectFile -PathType Leaf)) {
    throw "Projeto principal não encontrado: $projectFile"
}
if (-not (Test-Path $templateScript -PathType Leaf)) {
    throw "Template do instalador não encontrado: $templateScript"
}

[xml]$project = Get-Content $projectFile
$version = [string]$project.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version) -or $version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') {
    throw "Versão do projeto fora do formato MAJOR.MINOR.PATCH: '$version'."
}
$parsedVersion = [version]$version
if ($parsedVersion.Revision -ne -1) {
    throw "A versão do projeto precisa ter exatamente três componentes: '$version'."
}
$versionQuad = "$version.0"

$isccCandidates = @(
    (Join-Path $env:ProgramFiles(x86) "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    throw "Inno Setup 6 (ISCC.exe) não foi encontrado."
}
if (-not (Test-Path (Join-Path $dist "ynoez.exe") -PathType Leaf)) {
    throw "dist\ynoez.exe não existe. Publique o aplicativo antes."
}

New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null

# Remove only old installers to ensure the current build cannot accidentally upload a stale version.
Get-ChildItem -Path $artifactDir -Filter "ynoez-setup-*.exe" -File -ErrorAction SilentlyContinue |
    Remove-Item -Force

$template = [System.IO.File]::ReadAllText($templateScript)
$tokens = @("__APP_VERSION__", "__APP_VERSION_QUAD__")
foreach ($token in $tokens) {
    if (-not $template.Contains($token)) {
        throw "O template não contém o marcador obrigatório $token."
    }
}
if ([regex]::Matches($template, [regex]::Escape("__APP_VERSION__")).Count -ne 2 -or
    [regex]::Matches($template, [regex]::Escape("__APP_VERSION_QUAD__")).Count -ne 1) {
    throw "Quantidade inesperada de marcadores de versão no template Inno Setup."
}

$generated = $template.Replace("__APP_VERSION_QUAD__", $versionQuad)
$generated = $generated.Replace("__APP_VERSION__", $version)
if ($generated.Contains("__APP_VERSION")) {
    throw "Restaram marcadores de versão não substituídos no script gerado."
}

try {
    [System.IO.File]::WriteAllText(
        $generatedScript,
        $generated,
        [System.Text.UTF8Encoding]::new($false))

    Write-Host "Versão única lida do csproj: $version"
    Write-Host "VersionInfoVersion literal: $versionQuad"
    & $iscc $generatedScript
    if ($LASTEXITCODE -ne 0) {
        throw "ISCC falhou com código $LASTEXITCODE."
    }

    $setup = Join-Path $artifactDir "ynoez-setup-$version.exe"
    if (-not (Test-Path $setup -PathType Leaf)) {
        throw "O instalador esperado não foi gerado: $setup"
    }

    $fileInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($setup)
    if ($fileInfo.FileVersion -ne $versionQuad) {
        throw "Versão PE do instalador ($($fileInfo.FileVersion)) difere do projeto ($versionQuad)."
    }
    if ($fileInfo.ProductName -ne "ynoez") {
        throw "Metadado ProductName do instalador não é ynoez."
    }

    Write-Host "Instalador $version gerado e metadados PE verificados: $setup"
}
finally {
    if (Test-Path $generatedScript -PathType Leaf) {
        Remove-Item $generatedScript -Force
    }
}
