[CmdletBinding()]
param([string]$ToolVersion = '4.1.13')

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot 'src\ynoez\ynoez.csproj'
$componentRoot = Join-Path $projectRoot 'src\ynoez'
$dist = Join-Path $projectRoot 'dist'
$artifactDir = Join-Path $projectRoot 'artifacts'
$manifestRoot = Join-Path $dist '_manifest'
$validationReport = Join-Path $artifactDir 'sbom-validation.json'

if (-not (Test-Path -LiteralPath $projectFile -PathType Leaf)) { throw "Projeto principal não encontrado: $projectFile" }
if (-not (Test-Path -LiteralPath (Join-Path $dist 'ynoez.exe') -PathType Leaf)) { throw 'dist\ynoez.exe não existe. Execute scripts/publish.ps1 antes.' }
if (-not (Test-Path -LiteralPath (Join-Path $componentRoot 'obj\project.assets.json') -PathType Leaf)) { throw 'project.assets.json não existe. Restaure/publique o projeto antes de gerar a SBOM.' }
if ($ToolVersion -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') { throw "Versão inválida do Microsoft SBOM Tool: $ToolVersion" }

[xml]$project = Get-Content -LiteralPath $projectFile
$version = [string]$project.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version) -or $version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') { throw "Versão do projeto inválida: '$version'." }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET SDK não foi encontrado; não é possível instalar o gerador de SBOM.' }

$tempRoot = if (-not [string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
    $env:RUNNER_TEMP
} elseif (-not [string]::IsNullOrWhiteSpace($env:TEMP)) {
    $env:TEMP
} else {
    [System.IO.Path]::GetTempPath()
}
$toolDirectory = Join-Path $tempRoot "ynoez-sbom-tool-$ToolVersion"
$toolExecutable = Join-Path $toolDirectory 'sbom-tool.exe'
$manifestFile = Join-Path $manifestRoot 'spdx_2.2\manifest.spdx.json'
$outputFile = Join-Path $artifactDir "ynoez-$version.spdx.json"

New-Item -ItemType Directory -Path $artifactDir -Force | Out-Null
if (Test-Path -LiteralPath $manifestRoot) { Remove-Item -LiteralPath $manifestRoot -Recurse -Force }
if (Test-Path -LiteralPath $toolDirectory) { Remove-Item -LiteralPath $toolDirectory -Recurse -Force }
if (Test-Path -LiteralPath $validationReport) { Remove-Item -LiteralPath $validationReport -Force }

try {
    New-Item -ItemType Directory -Path $toolDirectory -Force | Out-Null
    Write-Host "Instalando Microsoft.Sbom.DotNetTool $ToolVersion em diretório temporário."
    & dotnet tool install --tool-path $toolDirectory --version $ToolVersion Microsoft.Sbom.DotNetTool
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $toolExecutable -PathType Leaf)) {
        throw "Não foi possível instalar Microsoft.Sbom.DotNetTool $ToolVersion."
    }

    Write-Host "Gerando SBOM SPDX 2.2 para ynoez $version."
    $generateArguments = @(
        'generate',
        '-b', $dist,
        '-bc', $componentRoot,
        '-pn', 'ynoez',
        '-pv', $version,
        '-ps', 'Organization: ynoez',
        '-nsb', 'https://github.com/6zyloov/ynoez-eternal-debloat'
    )
    & $toolExecutable @generateArguments
    if ($LASTEXITCODE -ne 0) { throw "Microsoft SBOM Tool falhou ao gerar a SBOM (código $LASTEXITCODE)." }
    if (-not (Test-Path -LiteralPath $manifestFile -PathType Leaf)) { throw "O gerador não produziu o manifesto esperado: $manifestFile" }

    $manifest = Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json
    if ($manifest.spdxVersion -ne 'SPDX-2.2') { throw "Versão SPDX inesperada: '$($manifest.spdxVersion)'." }
    if (@($manifest.packages).Count -lt 1) { throw 'A SBOM não contém nenhum pacote detectado; recusando publicar um manifesto vazio.' }
    if ([string]::IsNullOrWhiteSpace([string]$manifest.documentNamespace)) { throw 'A SBOM não contém documentNamespace SPDX.' }

    Write-Host 'Validando o manifesto com o próprio Microsoft SBOM Tool.'
    & $toolExecutable validate -b $dist -o $validationReport -mi 'SPDX:2.2'
    if ($LASTEXITCODE -ne 0) { throw "A validação SPDX 2.2 falhou (código $LASTEXITCODE)." }

    Copy-Item -LiteralPath $manifestFile -Destination $outputFile -Force
    $copied = Get-Content -LiteralPath $outputFile -Raw | ConvertFrom-Json
    if ($copied.spdxVersion -ne 'SPDX-2.2' -or @($copied.packages).Count -lt 1) { throw 'A cópia final da SBOM falhou na verificação estrutural.' }

    $hash = (Get-FileHash -LiteralPath $outputFile -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "SBOM SPDX 2.2 gerada e validada: $outputFile"
    Write-Host "Pacotes detectados: $(@($copied.packages).Count)"
    Write-Host "SHA-256: $hash" -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $manifestRoot) { Remove-Item -LiteralPath $manifestRoot -Recurse -Force }
    if (Test-Path -LiteralPath $validationReport) { Remove-Item -LiteralPath $validationReport -Force }
    if (Test-Path -LiteralPath $toolDirectory) { Remove-Item -LiteralPath $toolDirectory -Recurse -Force }
}
