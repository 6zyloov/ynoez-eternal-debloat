[CmdletBinding()]
param(
    [string]$InstallerPath
)

$ErrorActionPreference = 'Stop'

if ($env:GITHUB_ACTIONS -ne 'true') {
    throw 'Este teste de instalação é exclusivo de um runner GitHub Actions descartável.'
}

$target = 'C:\ynoez'
if (Test-Path -LiteralPath $target) {
    throw "O caminho $target já existe no runner. O teste foi bloqueado para não tocar em dados preexistentes."
}

$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($InstallerPath)) {
    $candidates = @(Get-ChildItem -Path (Join-Path $projectRoot 'artifacts') -Filter 'ynoez-setup-*.exe' -File)
    if ($candidates.Count -ne 1) {
        throw "Esperado exatamente um instalador em artifacts; encontrados: $($candidates.Count)."
    }
    $InstallerPath = $candidates[0].FullName
}
if (-not (Test-Path -LiteralPath $InstallerPath -PathType Leaf)) {
    throw "Instalador não encontrado: $InstallerPath"
}

$folders = @('Data', 'Backups', 'Logs', 'Downloads')
$markers = @{}
foreach ($folder in $folders) {
    $markers[$folder] = Join-Path (Join-Path $target $folder) '.ynoez-install-smoke-preserve'
}

function Invoke-SilentInstaller {
    param([Parameter(Mandatory)][string]$Path)

    $process = Start-Process -FilePath $Path -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-') -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw "O instalador terminou com código $($process.ExitCode)."
    }
}

$cleanupRoot = $true
try {
    Write-Host 'Etapa 1/3: instalação limpa em C:\ynoez.'
    Invoke-SilentInstaller -Path $InstallerPath

    $application = Join-Path $target 'ynoez.exe'
    $uninstaller = Join-Path $target 'unins000.exe'
    if (-not (Test-Path -LiteralPath $application -PathType Leaf)) {
        throw 'A instalação terminou sem produzir C:\ynoez\ynoez.exe.'
    }
    if (-not (Test-Path -LiteralPath $uninstaller -PathType Leaf)) {
        throw 'O instalador não registrou o desinstalador esperado.'
    }

    foreach ($folder in $folders) {
        $directory = Join-Path $target $folder
        if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
            throw "A pasta persistente $directory não foi criada."
        }
        Set-Content -LiteralPath $markers[$folder] -Value "Preservar $folder" -Encoding UTF8
    }

    Write-Host 'Etapa 2/3: reinstalação do mesmo setup sobre a instalação existente, preservando dados.'
    Invoke-SilentInstaller -Path $InstallerPath
    if (-not (Test-Path -LiteralPath $application -PathType Leaf)) {
        throw 'A atualização removeu o executável instalado.'
    }
    foreach ($folder in $folders) {
        if (-not (Test-Path -LiteralPath $markers[$folder] -PathType Leaf)) {
            throw "A atualização apagou dados existentes em $folder."
        }
    }

    Write-Host 'Etapa 3/3: desinstalação e verificação de preservação.'
    $uninstallProcess = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-') -Wait -PassThru
    if ($uninstallProcess.ExitCode -ne 0) {
        throw "O desinstalador terminou com código $($uninstallProcess.ExitCode)."
    }
    if (Test-Path -LiteralPath $application) {
        throw 'A desinstalação deixou ynoez.exe instalado.'
    }
    foreach ($folder in $folders) {
        if (-not (Test-Path -LiteralPath $markers[$folder] -PathType Leaf)) {
            throw "A desinstalação apagou dados do usuário em $folder."
        }
    }

    Write-Host 'Teste de instalação, atualização e desinstalação concluído; Data/Backups/Logs/Downloads preservados.' -ForegroundColor Green
}
finally {
    if ($cleanupRoot -and (Test-Path -LiteralPath $target)) {
        Remove-Item -LiteralPath $target -Recurse -Force
    }
}
